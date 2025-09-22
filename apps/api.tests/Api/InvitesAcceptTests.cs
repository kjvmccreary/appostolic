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
    /// <summary>
    /// Create an HttpClient and attach a tenant-scoped JWT for the provided email & tenant slug.
    /// Replaces legacy dev header auth usage during migration.
    /// </summary>
    private static async Task<HttpClient> CreateTenantAuthedClientAsync(WebAppFactory f, string email, string tenantSlug)
    {
        var c = f.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(c, email, tenantSlug);
        return c;
    }

    [Fact]
    public async Task Accept_SignedIn_User_Matches_Invite_Creates_Membership()
    {
    var clientOwner = await CreateTenantAuthedClientAsync(_factory, "kevin@example.com", "kevin-personal");

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

        // Seed the invited user (simulating they already have an account) and sign them in via dev headers
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Email == inviteeEmail))
            {
                db.Users.Add(new User { Id = Guid.NewGuid(), Email = inviteeEmail, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }

    var invitedClient = await CreateTenantAuthedClientAsync(_factory, inviteeEmail, "kevin-personal");

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
    var client = await CreateTenantAuthedClientAsync(_factory, "kevin@example.com", "kevin-personal");
        var resp = await client.PostAsJsonAsync("/api/invites/accept", new { token = "not-a-real-token" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
