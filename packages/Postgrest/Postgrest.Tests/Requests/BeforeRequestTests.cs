using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Supabase.Postgrest;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Postgrest.Tests.Requests;

/// <summary>
///     <see cref="ClientOptions.BeforeRequest" /> runs before a request reads its headers, for tables and for
///     RPC, so credentials a host renews inside it are the ones sent; a request keeps its query and range while
///     the hook is awaited, and a hook that fails stops the request.
/// </summary>
[TestClass]
[TestCategory("Contract")]
public class BeforeRequestTests
{
    private WireMockServer server = null!;

    [Table("todos")]
    private class Todo : BaseModel
    {
        [PrimaryKey("id", false)] public int Id { get; set; }
        [Column("name")] public string? Name { get; set; }
    }

    [TestInitialize]
    public void SetUp()
    {
        this.server = WireMockServer.Start();
        this.server.Given(Request.Create().UsingAnyMethod())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("[]"));
    }

    [TestCleanup]
    public void TearDown() => this.server.Stop();

    [TestMethod]
    public async Task Get_ShouldSendTheHeadersReadAfterBeforeRequest()
    {
        await this.RenewingClient().Table<Todo>().Get();
        this.SentHeader("Authorization").Should().Be("Bearer renewed", "the hook renews the credentials the header callback then reads");
    }

    [TestMethod]
    public async Task Rpc_ShouldSendTheHeadersReadAfterBeforeRequest()
    {
        await this.RenewingClient().Rpc("a_function", null);
        this.SentHeader("Authorization").Should().Be("Bearer renewed");
    }

    [TestMethod]
    public async Task Get_ShouldKeepItsQueryAndRange_GivenBeforeRequestYields()
    {
        await this.RenewingClient().Table<Todo>().Filter("id", Constants.Operator.Equals, "1").Range(0, 9).Get();
        var request = this.server.LogEntries.Single().RequestMessage!;
        request.Query!.Should().ContainKey("id").WhoseValue.Should().Contain("eq.1");
        this.SentHeader("Range").Should().Be("0-9");
    }

    [TestMethod]
    public async Task Get_ShouldNotSend_GivenBeforeRequestThrows()
    {
        var client = new Client(this.server.Url!, new ClientOptions
        {
            BeforeRequest = _ => throw new InvalidOperationException("no credentials"),
        });
        var act = () => client.Table<Todo>().Get();
        await act.Should().ThrowAsync<InvalidOperationException>();
        this.server.LogEntries.Should().BeEmpty("a request whose credentials could not be prepared must not leave");
    }

    [TestMethod]
    public async Task Get_ShouldSendOnce_GivenNoBeforeRequest()
    {
        await new Client(this.server.Url!).Table<Todo>().Get();
        this.server.LogEntries.Should().ContainSingle();
    }

    private Client RenewingClient()
    {
        var token = "stale";
        return new Client(this.server.Url!, new ClientOptions
        {
            BeforeRequest = async _ =>
            {
                await Task.Yield();
                token = "renewed";
            },
        })
        {
            GetHeaders = () => new Dictionary<string, string> { { "Authorization", $"Bearer {token}" } },
        };
    }

    private string SentHeader(string name) => this.server.LogEntries.Single().RequestMessage!.Headers!.Should().ContainKey(name).WhoseValue.Single();
}
