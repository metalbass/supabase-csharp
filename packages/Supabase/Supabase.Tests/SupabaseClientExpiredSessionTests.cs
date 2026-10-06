using System;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Supabase.Tests.Models;

namespace Supabase.Tests;

/// <summary>
/// Against the live Supabase CLI stack: a session whose access token expired while no refresh could run (a
/// device asleep, a tab frozen) is refreshed before the next request, so the request lands instead of being
/// refused with "JWT expired". The expired token is minted with the stack's JWT secret, as the Gotrue suite
/// mints its service-role token, so the server really rejects it.
/// </summary>
[TestClass]
[TestCategory("E2E")]
public class SupabaseClientExpiredSessionTests
{
    private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6ImFub24iLCJleHAiOjE5ODM4MTI5OTZ9.CRXP1A7WOeoJeXxjNni43kdQwgnWNReilDMblYTn_I0";
    private const string JwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";
    private const string Password = "I@M@SuperP@ssWord";

    private Supabase.Client client = null!;

    [TestInitialize]
    public async Task InitializeTest()
    {
        this.client = new Supabase.Client("http://localhost", AnonKey, new SupabaseOptions
        {
            AuthUrlFormat = "{0}:54321/auth/v1",
            RestUrlFormat = "{0}:54321/rest/v1",
            RealtimeUrlFormat = "ws://127.0.0.1:54321/realtime/v1",
            AutoConnectRealtime = false,
            AutoRefreshToken = false,
        });
        await this.client.InitializeAsync();
        await this.client.Auth.SignUp($"{Guid.NewGuid():N}@supabase.io", Password);
    }

    [TestMethod]
    public async Task From_ShouldInsert_GivenTheSessionsAccessTokenExpired()
    {
        var session = this.client.Auth.CurrentSession!;
        var staleRefreshToken = session.RefreshToken;
        session.AccessToken = ExpiredCopyOf(session.AccessToken!);
        session.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var inserted = await this.client.From<Channel>().Insert(new Channel { Slug = Guid.NewGuid().ToString() });
        inserted.Models.Should().ContainSingle("the request refreshed the session first instead of being refused with JWT expired");
        this.client.Auth.CurrentSession!.RefreshToken.Should().NotBe(staleRefreshToken, "the refresh rotated the session");
    }

    private static string ExpiredCopyOf(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = handler.ReadJwtToken(accessToken).Payload;
        var expired = new JwtPayload();
        foreach (var claim in claims)
        {
            expired[claim.Key] = claim.Value;
        }
        expired["exp"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        expired["iat"] = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        return handler.WriteToken(new JwtSecurityToken(new JwtHeader(credentials), expired));
    }
}
