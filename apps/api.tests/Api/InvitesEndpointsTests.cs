using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class InvitesEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public InvitesEndpointsTests(WebAppFactory factory) => _factory = factory;

    // RDH Story 2: use JWT helper instead of dev headers
    private static async Task<HttpClient> CreateAuthedClientAsync(WebAppFactory f)
    {
        var c = f.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(c, "kevin@example.com", "kevin-personal");
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
    public async Task Invites_Full_Lifecycle_Create_Resend_Accept_Revoke()
    {
    var client = await CreateAuthedClientAsync(_factory);
        var tenantId = await GetTenantIdAsync(_factory);

        // Start with clean slate for a unique email
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        // List initially
        var list0 = await client.GetAsync($"/api/tenants/{tenantId}/invites");
        list0.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr0 = await list0.Content.ReadFromJsonAsync<JsonElement>();
        arr0.ValueKind.Should().Be(JsonValueKind.Array);
        arr0.EnumerateArray().Any(e => e.GetProperty("email").GetString() == email).Should().BeFalse();

        // Create using roles flags (legacy single 'role' field deprecated)
    var create = new { email, roles = new[] { "TenantAdmin", "Creator" } }; // include multiple valid flag names to ensure flags roundtrip
        var createResp = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", create);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("email").GetString().Should().Be(email);
        // roles serialized as concatenated string or array? Current API returns aggregated string and rolesValue bitmask alongside (see other tests)
        // Accept either array or string representation by probing
        if (created.TryGetProperty("roles", out var rolesProp))
        {
            if (rolesProp.ValueKind == JsonValueKind.String)
            {
                var rolesStr = rolesProp.GetString();
                rolesStr.Should().Contain("TenantAdmin").And.Contain("Creator");
            }
            else if (rolesProp.ValueKind == JsonValueKind.Array)
            {
                var rolesArr = rolesProp.EnumerateArray().Select(e => e.GetString()).ToList();
                rolesArr.Should().Contain(new[] { "TenantAdmin", "Creator" });
            }
        }
        if (created.TryGetProperty("rolesValue", out var rolesValueProp) && rolesValueProp.ValueKind == JsonValueKind.Number)
        {
            var rolesValue = rolesValueProp.GetInt32();
            rolesValue.Should().BeGreaterThan(0); // precise bitmask validated in dedicated flags tests
        }
        var createdExpires = created.GetProperty("expiresAt").GetDateTime();
        createdExpires.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));

        // List shows it with metadata
        var list1 = await client.GetAsync($"/api/tenants/{tenantId}/invites");
        list1.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr1 = await list1.Content.ReadFromJsonAsync<JsonElement>();
        var item1 = arr1.EnumerateArray().FirstOrDefault(e => e.GetProperty("email").GetString() == email);
        item1.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        // Validate roles flags representation on listing
        if (item1.TryGetProperty("roles", out var listRoles))
        {
            if (listRoles.ValueKind == JsonValueKind.String)
            {
                var rolesStr = listRoles.GetString();
                rolesStr.Should().Contain("TenantAdmin").And.Contain("Creator");
            }
            else if (listRoles.ValueKind == JsonValueKind.Array)
            {
                var rolesArr = listRoles.EnumerateArray().Select(e => e.GetString()).ToList();
                rolesArr.Should().Contain(new[] { "TenantAdmin", "Creator" });
            }
        }
        item1.GetProperty("invitedByEmail").GetString().Should().Be("kevin@example.com");
        item1.GetProperty("acceptedAt").ValueKind.Should().Be(JsonValueKind.Null);

        // Resend updates expiry and clears acceptance (still null here)
        await Task.Delay(5); // ensure clock tick
        var resendResp = await client.PostAsync($"/api/tenants/{tenantId}/invites/{Uri.EscapeDataString(email)}/resend", content: null);
        resendResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resend = await resendResp.Content.ReadFromJsonAsync<JsonElement>();
        var newExpires = resend.GetProperty("expiresAt").GetDateTime();
        newExpires.Should().BeAfter(createdExpires);

        // Fetch token from DB to simulate acceptance via signup
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invitations.AsNoTracking().FirstAsync(i => i.TenantId == tenantId && i.Email.ToLower() == email.ToLower());
            token = inv.Token!;
        }

        // Accept via signup using invite token
        var signup = new { email = email, password = "Password123!", inviteToken = token };
        var signupResp = await client.PostAsJsonAsync("/api/auth/signup", signup);
        signupResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // List shows acceptedAt set and acceptedByEmail equals invitee
        var list2 = await client.GetAsync($"/api/tenants/{tenantId}/invites");
        list2.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr2 = await list2.Content.ReadFromJsonAsync<JsonElement>();
        var item2 = arr2.EnumerateArray().First(e => e.GetProperty("email").GetString() == email);
        item2.GetProperty("acceptedAt").ValueKind.Should().Be(JsonValueKind.String);
        item2.GetProperty("acceptedByEmail").GetString().Should().Be(email);

        // Revoke (delete)
        var delResp = await client.DeleteAsync($"/api/tenants/{tenantId}/invites/{Uri.EscapeDataString(email)}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Ensure it's gone
        var list3 = await client.GetAsync($"/api/tenants/{tenantId}/invites");
        list3.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr3 = await list3.Content.ReadFromJsonAsync<JsonElement>();
        arr3.EnumerateArray().Any(e => e.GetProperty("email").GetString() == email).Should().BeFalse();
    }
}
