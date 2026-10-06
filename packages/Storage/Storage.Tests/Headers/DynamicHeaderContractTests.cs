using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Supabase.Storage;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Storage.Tests.Headers;

/// <summary>
///     <see cref="StorageBucketApi.GetHeaders" /> is awaited per request, for the bucket API and for the file API a
///     bucket hands out, so credentials renewed after construction are the ones on the wire.
/// </summary>
[TestClass]
[TestCategory("Contract")]
public class DynamicHeaderContractTests
{
    private WireMockServer server = null!;

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
    public async Task ListBuckets_ShouldSendTheHeadersTheProviderReturnsAtRequestTime()
    {
        var token = "stale";
        var client = new Client($"{this.server.Url}/storage/v1") { GetHeaders = _ => Task.FromResult(new Dictionary<string, string> { { "Authorization", $"Bearer {token}" } }) };
        token = "renewed";
        await client.ListBuckets();
        this.SentHeader("Authorization").Should().Be("Bearer renewed", "the provider runs when the request is made, not when the client is built");
    }

    [TestMethod]
    public async Task From_ShouldSendTheHeadersTheProviderReturnsAtRequestTime()
    {
        var token = "stale";
        var client = new Client($"{this.server.Url}/storage/v1") { GetHeaders = _ => Task.FromResult(new Dictionary<string, string> { { "Authorization", $"Bearer {token}" } }) };
        var files = client.From("bucket");
        token = "renewed";
        await files.List();
        this.SentHeader("Authorization").Should().Be("Bearer renewed", "a file API built earlier must not keep a snapshot of the credentials");
    }

    private string SentHeader(string name) => this.server.LogEntries.Single().RequestMessage!.Headers!.Should().ContainKey(name).WhoseValue.Single();
}
