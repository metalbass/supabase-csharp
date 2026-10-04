#region

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gotrue.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using Supabase.Gotrue.Interfaces;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using static Supabase.Gotrue.Exceptions.FailureHint.Reason;

#endregion

namespace Gotrue.Tests.TokenRefresh;

/// <summary>
///     <c>GetSessionAsync</c> is what a request reads its credentials from, as <c>getSession()</c> is in
///     auth-js: a session whose access token has expired, or expires within the 90 second margin, is refreshed
///     first, concurrent callers share that one refresh, and a fresh session comes back without a request.
/// </summary>
[TestClass]
[TestCategory("Contract")]
public class GetSessionContractTests
{
    private const string AccessToken = "an-access-token";
    private const string RefreshTokenValue = "a-refresh-token";
    private IGotrueClient<User, Session> client = null!;
    private MockGotrueServer server = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        this.server = new MockGotrueServer();
        this.client = TestClients.Against(this.server);
    }

    [TestCleanup]
    public void TestCleanup() => this.server.Dispose();

    [TestMethod]
    public async Task GetSessionAsync_ShouldReturnNull_GivenNoSession()
    {
        (await this.client.GetSessionAsync()).Should().BeNull();
        this.server.CountReceivedRequests().Should().Be(0);
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldReturnTheSessionWithoutARequest_GivenAFreshToken()
    {
        this.LoadSession(expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        var session = await this.client.GetSessionAsync();
        session!.AccessToken.Should().Be(AccessToken);
        this.server.CountReceivedRequests().Should().Be(0, "a token the server still accepts needs no refresh");
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldRefreshFirst_GivenAnExpiredToken()
    {
        this.LoadSession(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        this.MockSuccessResponse();
        var session = await this.client.GetSessionAsync();
        session!.AccessToken.Should().Be("new-access-token", "the expired token was refreshed before being handed out");
        this.server.VerifySingleReceivedRequest().WithPath("/token").WithQueryParam("grant_type", "refresh_token");
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldRefreshFirst_GivenATokenExpiringWithinTheMargin()
    {
        this.LoadSession(expiresAt: DateTimeOffset.UtcNow.AddSeconds(30));
        this.MockSuccessResponse();
        (await this.client.GetSessionAsync())!.AccessToken.Should().Be("new-access-token",
            "auth-js refreshes within EXPIRY_MARGIN_MS (90 s) so a request does not race the expiry");
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldRefreshFirst_GivenNoExpiresAtAndAStaleCreatedAt()
    {
        this.LoadSession(expiresAt: null, createdAt: DateTime.UtcNow.AddHours(-2));
        this.MockSuccessResponse();
        (await this.client.GetSessionAsync())!.AccessToken.Should().Be("new-access-token",
            "without expires_at the expiry is created_at plus expires_in, as the refresh timer computes it");
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldReturnTheSessionAsIs_GivenOffline()
    {
        this.LoadSession(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        this.client.Online = false;
        (await this.client.GetSessionAsync())!.AccessToken.Should().Be(AccessToken);
        this.server.CountReceivedRequests().Should().Be(0, "a refresh cannot run offline");
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldShareOneRefresh_GivenConcurrentCallers()
    {
        this.LoadSession(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        this.MockSuccessResponse(delay: TimeSpan.FromMilliseconds(300));
        var sessions = await Task.WhenAll(this.client.GetSessionAsync(), this.client.GetSessionAsync(), this.client.GetSessionAsync());
        sessions.Should().OnlyContain(session => session!.AccessToken == "new-access-token");
        this.server.CountReceivedRequests("/token").Should().Be(1, "refresh tokens are single use, so callers must share the attempt in flight");
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldThrowInvalidRefreshTokenAndDestroySession_GivenRejectedRefresh()
    {
        this.LoadSession(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        this.MockErrorResponse(400, Fixture("token_not_found_error.json"));
        var act = () => this.client.GetSessionAsync();
        (await act.Should().ThrowAsync<GotrueException>()).Which.Reason.Should().Be(InvalidRefreshToken);
        this.client.CurrentSession.Should().BeNull();
    }

    [TestMethod]
    public async Task GetSessionAsync_ShouldStopWaiting_GivenCancellation()
    {
        this.LoadSession(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        this.MockSuccessResponse(delay: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = () => this.client.GetSessionAsync(cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private void LoadSession(DateTimeOffset? expiresAt, DateTime? createdAt = null)
    {
        var persistence = SessionPersistenceSubstitute.Tracking();
        persistence.SaveSession(new Session
        {
            AccessToken = AccessToken,
            RefreshToken = RefreshTokenValue,
            ExpiresIn = 3600,
            ExpiresAt = expiresAt?.ToUnixTimeSeconds(),
            CreatedAt = createdAt ?? DateTime.UtcNow,
        });
        this.client.SetPersistence(persistence);
        this.client.LoadSession();
    }

    private void MockSuccessResponse(TimeSpan? delay = null)
    {
        var response = Response.Create()
            .WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(Fixture("token_success.json"));
        if (delay is { } d)
        {
            response = response.WithDelay(d);
        }
        this.server.Given(Request.Create().WithPath("/token").UsingPost()).RespondWith(response);
    }

    private void MockErrorResponse(int statusCode, string body) =>
        this.server
            .Given(Request.Create().WithPath("/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TokenRefresh", "Fixtures", name));
}
