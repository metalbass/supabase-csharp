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
/// The umbrella wires Postgrest's <c>BeforeRequest</c> to Auth, so a request first asks for a session whose
/// token the server accepts (auth-js: every fetch reads its token through <c>getSession()</c>). A refresh that
/// fails lets the request go ahead and be refused by the server, as auth-js does.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class SupabaseClientSessionRefreshTests
{
    private static Supabase.Client UrlClient() =>
        new("http://localhost", "test-key", new SupabaseOptions { AutoConnectRealtime = false });

    [TestMethod]
    public async Task SupabaseClient_ShouldAskAuthForTheSessionBeforeAPostgrestRequest()
    {
        var client = UrlClient();
        var auth = Substitute.For<IGotrueClient<User, Session>>();
        client.Auth = auth;
        await client.Postgrest.Options.BeforeRequest!(CancellationToken.None);
        await auth.Received(1).GetSessionAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SupabaseClient_ShouldLetTheRequestGoAhead_GivenTheRefreshFails()
    {
        var client = UrlClient();
        var auth = Substitute.For<IGotrueClient<User, Session>>();
        auth.GetSessionAsync(Arg.Any<CancellationToken>()).Returns<Session?>(_ => throw new GotrueException("rejected"));
        client.Auth = auth;
        var act = () => client.Postgrest.Options.BeforeRequest!(CancellationToken.None);
        await act.Should().NotThrowAsync("the server's own refusal is the error the caller should see, as in auth-js");
    }

    [TestMethod]
    public async Task SupabaseClient_ShouldPassTheCancellationTokenToAuth()
    {
        var client = UrlClient();
        var auth = Substitute.For<IGotrueClient<User, Session>>();
        client.Auth = auth;
        using var cancellation = new CancellationTokenSource();
        await client.Postgrest.Options.BeforeRequest!(cancellation.Token);
        await auth.Received(1).GetSessionAsync(cancellation.Token);
    }
}
