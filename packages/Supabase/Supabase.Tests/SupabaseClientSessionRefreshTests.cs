using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using Supabase.Gotrue.Interfaces;

namespace Supabase.Tests;

/// <summary>
/// The header provider the umbrella gives Postgrest, Storage, Functions and Realtime first asks Auth for a session
/// whose token the server accepts (auth-js: every fetch reads its token through <c>getSession()</c>). A refresh that
/// fails lets the request go ahead and be refused by the server. Auth's own provider never asks, since a refresh must
/// not wait for itself.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class SupabaseClientSessionRefreshTests
{
    private static Supabase.Client UrlClient() =>
        new("http://localhost", "test-key", new SupabaseOptions { AutoConnectRealtime = false });

    private static IGotrueClient<User, Session> SubstituteAuth(Supabase.Client client)
    {
        var auth = Substitute.For<IGotrueClient<User, Session>>();
        client.Auth = auth;
        return auth;
    }

    [TestMethod]
    public async Task PostgrestHeaders_ShouldAskAuthForTheSessionFirst()
    {
        var client = UrlClient();
        var auth = SubstituteAuth(client);
        await client.Postgrest.GetHeaders!(CancellationToken.None);
        await auth.Received(1).GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StorageFunctionsAndRealtimeHeaders_ShouldAskAuthForTheSessionFirst()
    {
        var client = UrlClient();
        var auth = SubstituteAuth(client);
        await client.Storage.GetHeaders!(CancellationToken.None);
        await client.Functions.GetHeaders!(CancellationToken.None);
        await client.Realtime.GetHeaders!(CancellationToken.None);
        await auth.Received(3).GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AuthHeaders_ShouldNotAskForTheSession()
    {
        var client = UrlClient();
        var authProvider = client.Auth.GetHeaders!;
        var auth = SubstituteAuth(client);
        await authProvider(CancellationToken.None);
        await auth.DidNotReceive().GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task PostgrestHeaders_ShouldStillBeReturned_GivenTheRefreshFails()
    {
        var client = UrlClient();
        var auth = SubstituteAuth(client);
        auth.GetSessionAsync(Arg.Any<CancellationToken>()).Returns<Session?>(_ => throw new GotrueException("rejected"));
        var headers = await client.Postgrest.GetHeaders!(CancellationToken.None);
        headers.Should().ContainKey("apiKey", "the server's own refusal is the error the caller should see, as in auth-js");
    }

    [TestMethod]
    public async Task PostgrestHeaders_ShouldCarryTheRefreshedToken()
    {
        var client = UrlClient();
        var auth = SubstituteAuth(client);
        auth.CurrentSession.Returns(new Session { AccessToken = "fresh-token" });
        (await client.Postgrest.GetHeaders!(CancellationToken.None))["Authorization"].Should().Be("Bearer fresh-token");
    }

    [TestMethod]
    public async Task PostgrestHeaders_ShouldPassTheCancellationTokenToAuth()
    {
        var client = UrlClient();
        var auth = SubstituteAuth(client);
        using var cancellation = new CancellationTokenSource();
        await client.Postgrest.GetHeaders!(cancellation.Token);
        await auth.Received(1).GetSessionAsync(cancellation.Token);
    }
}
