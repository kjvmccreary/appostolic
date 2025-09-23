using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Appostolic.Api.Application.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Appostolic.Api.AuthTests; // TestAuthSeeder
using Appostolic.Api.Tests.TestUtilities;

namespace Appostolic.Api.Tests.Api;

public class UserPasswordEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public UserPasswordEndpointsTests(WebAppFactory factory) => _factory = factory;

    // Story: JWT auth refactor – migrate password endpoint tests off legacy login/select flow.
    // We now mint a tenant-scoped token via TestAuthSeeder, then directly seed the initial password
    // hash for that newly created user so the password change endpoint can validate current password.
    // Each test uses an isolated email + tenant slug to avoid cross-test state bleed.

    // Shared UniqueId now provides Frag/Slug/Email helpers

    private async Task<(HttpClient client, string email)> CreateOwnerClientWithPasswordAsync(string scenario, string initialPassword)
    {
    var email = UniqueId.Email("pwd");
    var slug = UniqueId.Slug(scenario);
        var (token, userId, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slug, owner: true);

        // Seed password hash for this user to match initialPassword
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            var (hash, salt, _) = hasher.HashPassword(initialPassword);
            var updated = user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
            db.Users.Attach(updated);
            var entry = db.Entry(updated);
            entry.Property(u => u.PasswordHash).IsModified = true;
            entry.Property(u => u.PasswordSalt).IsModified = true;
            entry.Property(u => u.PasswordUpdatedAt).IsModified = true;
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, email);
    }

    [Fact]
    public async Task Change_password_succeeds_with_valid_current()
    {
        var (client, _) = await CreateOwnerClientWithPasswordAsync("pwd-valid", "Password123!");
        var resp = await client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = "Password123!", newPassword = "NewPass123" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Change_password_rejects_invalid_current()
    {
        var (client, _) = await CreateOwnerClientWithPasswordAsync("pwd-invalid-current", "Password123!");
        var resp = await client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = "Wrong000", newPassword = "NewPass123" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Change_password_rejects_weak_password()
    {
        var (client, _) = await CreateOwnerClientWithPasswordAsync("pwd-weak", "Password123!");
        var resp = await client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = "Password123!", newPassword = "short" });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
