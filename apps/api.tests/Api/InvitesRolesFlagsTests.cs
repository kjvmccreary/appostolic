using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class InvitesRolesFlagsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public InvitesRolesFlagsTests(WebAppFactory factory) => _factory = factory;

    private static HttpClient CreateClientWithDevHeaders(WebAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        c.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
        return c;
    }

    private static async Task<Guid> GetTenantIdAsync(WebAppFactory factory, string slug = "kevin-personal")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var t = await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == slug);
        return t.Id;
    }

    [Fact]
    public async Task Create_Invite_With_Roles_Flags_Persists_And_Lists()
    {
        var client = CreateClientWithDevHeaders(_factory);
        var tenantId = await GetTenantIdAsync(_factory);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var payload = new { email, role = "Viewer", roles = new[] { "Creator", "Learner" } };
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

    [Fact]
    public async Task Create_Invite_Without_Roles_Derives_From_Legacy_Role()
    {
        var client = CreateClientWithDevHeaders(_factory);
        var tenantId = await GetTenantIdAsync(_factory);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var payload = new { email, role = "Admin" };
        var create = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", payload);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("rolesValue").GetInt32().Should().Be((int)(Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner));
    }

    [Fact]
    public async Task Accept_Invite_Sets_Membership_Roles_Flags()
    {
        var client = CreateClientWithDevHeaders(_factory);
        var tenantId = await GetTenantIdAsync(_factory);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var payload = new { email, role = "Viewer", roles = new[] { "Learner" } };
        var create = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", payload);
        create.EnsureSuccessStatusCode();

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invitations.AsNoTracking().FirstAsync(i => i.TenantId == tenantId && i.Email.ToLower() == email.ToLower());
            token = inv.Token!;
            // Ensure the invited user exists for accept flow using dev header auth
            if (!await db.Users.AsNoTracking().AnyAsync(u => u.Email == email))
            {
                db.Users.Add(new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }

        var invitedClient = _factory.CreateClient();
        invitedClient.DefaultRequestHeaders.Add("x-dev-user", email);
        invitedClient.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
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
        var client = CreateClientWithDevHeaders(_factory);
        var tenantId = await GetTenantIdAsync(_factory);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        var payload = new { email, roles = new[] { "NotARealRole" } };
        var resp = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
