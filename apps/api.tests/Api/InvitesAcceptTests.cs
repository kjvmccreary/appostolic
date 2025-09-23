using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // TestAuthSeeder

namespace Appostolic.Api.Tests.Api;

public class InvitesAcceptTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public InvitesAcceptTests(WebAppFactory factory) => _factory = factory;

    // Story 5 Refactor: Harness TestAuthSeeder for deterministic owner + invitee tokens.
    private static async Task<(HttpClient ownerClient, Guid tenantId)> CreateOwnerClientAsync(WebAppFactory factory, string email = "kevin@example.com", string tenantSlug = "kevin-personal")
    {
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(factory, email, tenantSlug, owner: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, tenantId);
    }

    [Fact]
    public async Task Accept_SignedIn_User_Matches_Invite_Creates_Membership()
    {
        // Owner acting client (tenant-selected) via direct token issuance
        var (clientOwner, tenantId) = await CreateOwnerClientAsync(_factory);

        // Arrange: create an invite for a brand-new user email under owner's tenant
        var inviteeEmail = $"invitee-{Guid.NewGuid():N}@example.com";

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

        // Invitee path: create account & acquire neutral token
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Email == inviteeEmail))
            {
                db.Users.Add(new User { Id = Guid.NewGuid(), Email = inviteeEmail, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        var (neutralToken, _) = await TestAuthSeeder.IssueNeutralTokenAsync(_factory, inviteeEmail);
        var invitedClient = _factory.CreateClient();
        invitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", neutralToken);

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
        var (client, _) = await CreateOwnerClientAsync(_factory);
        var resp = await client.PostAsJsonAsync("/api/invites/accept", new { token = "not-a-real-token" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
