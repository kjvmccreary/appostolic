using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Appostolic.Api.Tests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appostolic.Api.AuthTests;

public class MagicConsumeJwtTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public MagicConsumeJwtTests(WebAppFactory factory) => _factory = factory;

    private async Task<string> IssueMagicTokenAndGetPlainAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Re-use logic similar to request endpoint: create token record manually so we can know plaintext
        var token = Guid.NewGuid().ToString("N");
        var hash = HashToken(token);
        var now = DateTime.UtcNow;
        db.LoginTokens.Add(new LoginToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            TokenHash = hash,
            Purpose = "magic",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(15)
        });
        await db.SaveChangesAsync();
        return token;
    }

    private static string HashToken(string token)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public async Task MagicConsume_IssuesNeutralAndTenantTokens()
    {
        using var client = _factory.CreateClient();
        var email = $"magicjwt-{Guid.NewGuid():N}@example.com";
        // Manually provision a token (user doesn't exist yet)
        var token = await IssueMagicTokenAndGetPlainAsync(email);
        var res = await client.PostAsJsonAsync("/api/auth/magic/consume", new { token });
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonObject>();
        doc.Should().NotBeNull();
        doc!["access"]!.Should().NotBeNull();
        doc!["refresh"]!.Should().NotBeNull();
        doc!["tenantToken"]!.Should().NotBeNull(); // personal tenant auto-created
    }

    [Fact]
    public async Task MagicConsume_LegacyMode_ReturnsLegacyShape()
    {
        using var client = _factory.CreateClient();
        var email = $"magiclegacy-{Guid.NewGuid():N}@example.com";
        var token = await IssueMagicTokenAndGetPlainAsync(email);
        var res = await client.PostAsJsonAsync("/api/auth/magic/consume?includeLegacy=true", new { token });
        res.EnsureSuccessStatusCode();
        var legacy = await res.Content.ReadFromJsonAsync<JsonObject>();
        legacy.Should().NotBeNull();
        // Legacy shape: { user: { id, email }, memberships: [...] } and NO access/refresh/tenantToken keys
        var legacyUser = legacy!["user"]!.AsObject();
        legacyUser["id"].Should().NotBeNull();
        legacyUser["email"]!.GetValue<string>().Should().Be(email);
        legacy.ContainsKey("access").Should().BeFalse();
        legacy.ContainsKey("refresh").Should().BeFalse();
        legacy.ContainsKey("tenantToken").Should().BeFalse();
        legacy.ContainsKey("memberships").Should().BeTrue();
    }
}
