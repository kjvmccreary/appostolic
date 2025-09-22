using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Appostolic.Api.Application.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.Tests.Api;

public class UserPasswordEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public UserPasswordEndpointsTests(WebAppFactory factory) => _factory = factory;

    // RDH Story 2: helper mints JWT tokens now
    private static async Task<HttpClient> ClientAsync(WebAppFactory f, string seedPassword)
    {
        var c = f.CreateClient();
        // Seeding moved to SeedPasswordAsync so we avoid duplicate hashing; seedPassword should match helper default
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(f, c, "kevin@example.com", "kevin-personal");
        return c;
    }

    private async Task SeedPasswordAsync(string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "kevin@example.com");
        var (hash, salt, _) = hasher.HashPassword(password);
        var updated = user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
        db.Users.Attach(updated);
        var entry = db.Entry(updated);
        entry.Property(u => u.PasswordHash).IsModified = true;
        entry.Property(u => u.PasswordSalt).IsModified = true;
        entry.Property(u => u.PasswordUpdatedAt).IsModified = true;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Change_password_succeeds_with_valid_current()
    {
        await SeedPasswordAsync("Password123!");
    var client = await ClientAsync(_factory, "Password123!");
    var resp = await client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = "Password123!", newPassword = "NewPass123" });
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Change_password_rejects_invalid_current()
    {
        await SeedPasswordAsync("Password123!");
    var client = await ClientAsync(_factory, "Password123!");
        var resp = await client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = "Wrong000", newPassword = "NewPass123" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Change_password_rejects_weak_password()
    {
        await SeedPasswordAsync("Password123!");
    var client = await ClientAsync(_factory, "Password123!");
        var resp = await client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = "Password123!", newPassword = "short" });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
