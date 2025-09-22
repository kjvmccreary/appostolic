using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class InvitesAcceptTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public InvitesAcceptTests(WebAppFactory factory) => _factory = factory;
    // RDH Story 2 Phase A: real password -> login -> select tenant migration.
    private const string DefaultPw = "Password123!"; // must align with AuthTestClientFlow.DefaultPassword

    /// <summary>
    /// Seed (or overwrite) password hash for given user so we can exercise real /api/auth/login.
    /// </summary>
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
    public async Task Accept_SignedIn_User_Matches_Invite_Creates_Membership()
    {
        // Owner acting client (tenant-selected)
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var clientOwner = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, clientOwner, "kevin@example.com", "kevin-personal");

        // Arrange: create an invite for a brand-new user email under owner's tenant
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@example.com";

        // Get tenantId for owner
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var t = await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal");
            tenantId = t.Id;
        }

    // Legacy single 'role' field deprecated: provide roles flags array only.
    var createResp = await clientOwner.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", new { email = inviteeEmail, roles = new[] { "Creator", "Learner" } });
    createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Grab token
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invitations.AsNoTracking().FirstAsync(i => i.TenantId == tenantId && i.Email.ToLower() == inviteeEmail.ToLower());
            token = inv.Token!;
        }

        // Invitee path: create account (signup) or ensure password, then login neutrally (no tenant selection needed for accept)
        // Here we simulate the invited user already created an account before accepting.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Email == inviteeEmail))
            {
                db.Users.Add(new User { Id = Guid.NewGuid(), Email = inviteeEmail, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        await SeedPasswordAsync(inviteeEmail, DefaultPw);
        var invitedClient = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginNeutralAsync(_factory, invitedClient, inviteeEmail);

        // Act: accept invite while signed in
        var acceptResp = await invitedClient.PostAsJsonAsync("/api/invites/accept", new { token });
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var accept = await acceptResp.Content.ReadFromJsonAsync<JsonElement>();
    accept.GetProperty("tenantSlug").GetString().Should().Be("kevin-personal");
    // Response now conveys roles/rolesValue instead of legacy role string.
    accept.TryGetProperty("roles", out var rolesEl).Should().BeTrue();
    rolesEl.GetString()!.Should().Contain("Creator");
    accept.GetProperty("rolesValue").GetInt32().Should().Be((int)(Roles.Creator | Roles.Learner));
    accept.GetProperty("membershipCreated").GetBoolean().Should().BeTrue();

        // Assert: membership now exists
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invUser = await db.Users.AsNoTracking().FirstAsync(u => u.Email == inviteeEmail);
            var exists = await db.Memberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == invUser.Id);
            exists.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Accept_With_Invalid_Token_Fails()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");
        var resp = await client.PostAsJsonAsync("/api/invites/accept", new { token = "not-a-real-token" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
