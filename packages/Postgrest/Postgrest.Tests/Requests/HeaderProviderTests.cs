using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
///     <see cref="Client.GetHeaders" /> is awaited before each request is sent, for tables and for RPC, so the
///     credentials a host renews inside it are the ones on the wire; a request keeps its query and range while the
///     provider is awaited, and a provider that fails stops the request.
/// </summary>
[TestClass]
[TestCategory("Contract")]
public class HeaderProviderTests
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
    public async Task Get_ShouldSendTheHeadersTheProviderReturns_GivenAnAsyncProvider()
    {
        await this.RenewingClient().Table<Todo>().Get();
        this.SentHeader("Authorization").Should().Be("Bearer renewed", "the provider is awaited, so the credentials it renews are the ones sent");
    }

    [TestMethod]
    public async Task Rpc_ShouldSendTheHeadersTheProviderReturns_GivenAnAsyncProvider()
    {
        await this.RenewingClient().Rpc("a_function", null);
        this.SentHeader("Authorization").Should().Be("Bearer renewed");
    }

    [TestMethod]
    public async Task Get_ShouldKeepItsQueryAndRange_GivenTheProviderYields()
    {
        await this.RenewingClient().Table<Todo>().Filter("id", Constants.Operator.Equals, "1").Range(0, 9).Get();
        var request = this.server.LogEntries.Single().RequestMessage!;
        request.Query!.Should().ContainKey("id").WhoseValue.Should().Contain("eq.1");
        this.SentHeader("Range").Should().Be("0-9");
    }

    [TestMethod]
    public async Task Get_ShouldPassTheCancellationTokenToTheProvider()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken seen = default;
        var client = new Client(this.server.Url!)
        {
            GetHeaders = token =>
            {
                seen = token;
                return Task.FromResult(new Dictionary<string, string>());
            },
        };
        await client.Table<Todo>().Get(cancellation.Token);
        seen.Should().Be(cancellation.Token);
    }

    [TestMethod]
    public async Task Get_ShouldNotSend_GivenTheProviderThrows()
    {
        var client = new Client(this.server.Url!) { GetHeaders = _ => throw new InvalidOperationException("no credentials") };
        var act = () => client.Table<Todo>().Get();
        await act.Should().ThrowAsync<InvalidOperationException>();
        this.server.LogEntries.Should().BeEmpty("a request whose credentials could not be prepared must not leave");
    }

    private Client RenewingClient()
    {
        var token = "stale";
        return new Client(this.server.Url!)
        {
            GetHeaders = async _ =>
            {
                await Task.Yield();
                token = "renewed";
                return new Dictionary<string, string> { { "Authorization", $"Bearer {token}" } };
            },
        };
    }

    private string SentHeader(string name) => this.server.LogEntries.Single().RequestMessage!.Headers!.Should().ContainKey(name).WhoseValue.Single();
}
