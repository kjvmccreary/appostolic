using System.Net.Http.Json;
using Appostolic.Api.Tests;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.AuthTests;

public class LoginRolesConvergenceTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LoginRolesConvergenceTests(WebAppFactory factory) => _factory = factory;

    private sealed record MembershipDto(Guid tenantId, string tenantSlug, string role, int? roles);
    private sealed record LoginPayload(Guid Id, string Email, MembershipDto[] memberships);

    [Fact]
    public async Task Login_Corrects_MismatchedRolesBitmask()
    {
        using var client = _factory.CreateClient();
        var email = "rolesconverge@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // Directly tamper with membership to simulate historical bad data (Owner with wrong flags 6 instead of 15)
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
            var membership = await db.Memberships.FirstAsync(m => m.UserId == user.Id);
            membership.Roles = (Roles)6; // e.g., stale flags missing TenantAdmin & Learner
            await db.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginPayload>();
        payload.Should().NotBeNull();
        var m = payload!.memberships.Single();
        m.role.Should().Be("Owner");
        m.roles.Should().Be(15); // Runtime convergence should correct to full flags for Owner
    }
}
