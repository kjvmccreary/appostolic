using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Appostolic.Api.Tests; // WebAppFactory

namespace Appostolic.Api.AuthTests;

/// <summary>
/// Verifies that when the DISABLE_LEGACY_ROLE_COMPAT flag is set, the runtime login convergence
/// logic does NOT mutate a mismatched legacy/flags pair. This protects the staging validation phase
/// where we want to observe any lingering bad data instead of silently correcting it.
/// </summary>
public class LoginRolesConvergenceDisabledFlagTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LoginRolesConvergenceDisabledFlagTests(WebAppFactory factory)
    {
        _factory = factory;
        // Set the environment variable for the process (affects code under test reading Environment.GetEnvironmentVariable)
        Environment.SetEnvironmentVariable("DISABLE_LEGACY_ROLE_COMPAT", "true");
    }

    private sealed record MembershipDto(Guid tenantId, string tenantSlug, string role, int? roles);
    private sealed record LoginPayload(Guid Id, string Email, MembershipDto[] memberships);

    [Fact]
    public async Task Login_DoesNotCorrect_MismatchedRolesBitmask_WhenFlagEnabled()
    {
        using var client = _factory.CreateClient();
        var email = "rolesconverge-flag-disabled@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // Tamper membership to simulate stale/mismatched data (Owner with flags missing bits)
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        int tamperedValue = 6; // (Creator | Approver) for example, missing TenantAdmin & Learner relative to canonical 15
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
            var membership = await db.Memberships.FirstAsync(m => m.UserId == user.Id);
            membership.Roles = (Roles)tamperedValue;
            await db.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginPayload>();
        payload.Should().NotBeNull();
        var m = payload!.memberships.Single();
        m.role.Should().Be("Owner");
        m.roles.Should().Be(tamperedValue, "runtime convergence should be disabled by DISABLE_LEGACY_ROLE_COMPAT flag");
    }
}
