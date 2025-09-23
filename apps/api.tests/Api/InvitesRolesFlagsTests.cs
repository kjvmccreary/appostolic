using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // TestAuthSeeder

namespace Appostolic.Api.Tests.Api;

public class InvitesRolesFlagsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public InvitesRolesFlagsTests(WebAppFactory factory) => _factory = factory;

    // Story 5 Refactor: direct tenant-scoped token issuance for owner user.
    private static async Task<(HttpClient client, Guid tenantId)> CreateOwnerClientAsync(WebAppFactory factory, string email = "kevin@example.com", string tenantSlug = "kevin-personal")
    {
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(factory, email, tenantSlug, owner: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, tenantId);
    }

    [Fact]
    public async Task Create_Invite_With_Roles_Flags_Persists_And_Lists()
    {
    var (client, tenantId) = await CreateOwnerClientAsync(_factory);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        // Legacy 'role' field is now deprecated and rejected. Provide only flags.
        var payload = new { email, roles = new[] { "Creator", "Learner" } };
        var create = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", payload);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("roles").GetString()!.Should().Contain("Creator");
        created.GetProperty("rolesValue").GetInt32().Should().Be((int)(Roles.Creator | Roles.Learner));

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{tenantId}/invites");
        var item = list.EnumerateArray().First(e => e.GetProperty("email").GetString() == email);
        item.GetProperty("roles").GetString()!.Should().Contain("Creator");
        item.GetProperty("rolesValue").GetInt32().Should().Be((int)(Roles.Creator | Roles.Learner));
    }

    // Legacy derivation test removed: invites must now specify roles flags; legacy 'role' only is rejected.
    [Fact]
    public async Task Accept_Invite_Sets_Membership_Roles_Flags()
    {
    var (client, tenantId) = await CreateOwnerClientAsync(_factory);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var payload = new { email, roles = new[] { "Learner" } };
        var create = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", payload);
        create.EnsureSuccessStatusCode();

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invitations.AsNoTracking().FirstAsync(i => i.TenantId == tenantId && i.Email.ToLower() == email.ToLower());
            token = inv.Token!;
            // Ensure the invited user exists prior to acceptance (account creation path); now uses real neutral login flow
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Email == email))
            {
                db.Users.Add(new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }

        // Invitee performs neutral login (no tenant selection yet) before accepting invite
    // Invite acceptance: simulate invitee account creation & neutral auth by issuing a neutral token.
    var (neutralToken, _) = await TestAuthSeeder.IssueNeutralTokenAsync(_factory, email);
    var invitedClient = _factory.CreateClient();
    invitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", neutralToken);
        var accept = await invitedClient.PostAsJsonAsync("/api/invites/accept", new { token });
        accept.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
            var membership = await db.Memberships.AsNoTracking().FirstAsync(m => m.TenantId == tenantId && m.UserId == user.Id);
            ((int)membership.Roles).Should().Be((int)Roles.Learner);
        }
    }

    [Fact]
    public async Task Create_Invite_With_Invalid_Role_Flag_Is_400()
    {
    var (client, tenantId) = await CreateOwnerClientAsync(_factory);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        var payload = new { email, roles = new[] { "NotARealRole" } };
        var resp = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
