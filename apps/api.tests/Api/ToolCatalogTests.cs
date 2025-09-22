using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Appostolic.Api.AuthTests; // AuthTestClientFlow
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class ToolCatalogTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ToolCatalogTests(WebAppFactory factory) => _factory = factory;

    // RDH Story 2 Phase A: migrated from legacy mint helper to real password + login + select-tenant flow.
    private const string DefaultPw = "Password123!";
    private async Task SeedPasswordAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var (hash, salt, _) = hasher.HashPassword(password);
        db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Tools_Catalog_Lists_Registered_Tools_With_Categories()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient();
        await AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");

        var resp = await client.GetAsync("/api/agents/tools");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        payload.ValueKind.Should().Be(JsonValueKind.Array);

        var names = payload.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray();
        names.Should().BeEquivalentTo(new[] { "db.query", "fs.write", "web.search" });

        var cat = payload.EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("category").GetString());
        cat["web.search"].Should().Be("search");
        cat["db.query"].Should().Be("db");
        cat["fs.write"].Should().Be("fs");

        // Basic description presence
        foreach (var e in payload.EnumerateArray())
        {
            e.TryGetProperty("description", out var desc).Should().BeTrue();
            desc.ValueKind.Should().Be(JsonValueKind.String);
            desc.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
