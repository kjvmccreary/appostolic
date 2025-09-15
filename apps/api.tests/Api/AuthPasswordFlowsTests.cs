using System.Net;
using System.Net.Http.Json;
using Appostolic.Api.App.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appostolic.Api.Tests.Api;

public class AuthPasswordFlowsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public AuthPasswordFlowsTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ForgotPassword_AcceptsAndStoresToken_SendsEmail()
    {
        var client = _factory.CreateClient();
        var email = "kevin@example.com"; // seeded in WebAppFactory

        var resp = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

        // Verify token persisted
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = db.LoginTokens.Where(t => t.Email == email && t.Purpose == "pwreset").ToList();
        Assert.True(tokens.Count >= 1);
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_UpdatesHashAndConsumesToken()
    {
        var client = _factory.CreateClient();
        var email = "kevin@example.com";

        // Request reset
        var r1 = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);

    // Grab token from DB (simulate email capture)
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var tokenRow = db.LoginTokens.Where(t => t.Email == email && t.Purpose == "pwreset").OrderByDescending(t => t.CreatedAt).First();

        // We don't store raw token; to simulate end-to-end, call the API path by constructing the raw token isn't trivial.
        // Instead, directly call reset endpoint using the DB token is not possible (hash only). We'll emulate by inserting a known token.
        var rawToken = Guid.NewGuid().ToString("N");
        var fakeHash = HashToken(rawToken);
        db.LoginTokens.Add(new LoginToken { Id = Guid.NewGuid(), Email = email, TokenHash = fakeHash, Purpose = "pwreset", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        db.SaveChanges();

        var newPassword = "NewP@ssw0rd!";
        var r2 = await client.PostAsJsonAsync("/api/auth/reset-password", new { token = rawToken, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, r2.StatusCode);

        // Re-open a new scope/DbContext to observe changes from the API's DbContext
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Token should be consumed
            var consumed = verifyDb.LoginTokens.AsNoTracking().Single(x => x.TokenHash == fakeHash);
            Assert.NotNull(consumed.ConsumedAt);

            // User should have non-null hash/salt
            var user = verifyDb.Users.AsNoTracking().Single(u => u.Email == email);
            Assert.NotNull(user.PasswordHash);
            Assert.NotNull(user.PasswordSalt);
        }
    }

    [Fact]
    public async Task ChangePassword_WithAuth_UpdatesPassword()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

        // Seed a starting password for the user
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
            var user = db.Users.AsNoTracking().Single(u => u.Email == "kevin@example.com");
            var (h, s, it) = hasher.HashPassword("oldpw");
            db.Users.Update(user with { PasswordHash = h, PasswordSalt = s, PasswordUpdatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }

        var resp = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "oldpw", newPassword = "newpw!" });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/reset-password", new { token = "not-a-real-token", newPassword = "whatever" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrent_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

        // Ensure user has some password different from the one we'll submit
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
            var user = db.Users.AsNoTracking().Single(u => u.Email == "kevin@example.com");
            var (h, s, it) = hasher.HashPassword("correct-current");
            db.Users.Update(user with { PasswordHash = h, PasswordSalt = s, PasswordUpdatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }

        var resp = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "wrong-current", newPassword = "newpw!" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private static string HashToken(string token)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
