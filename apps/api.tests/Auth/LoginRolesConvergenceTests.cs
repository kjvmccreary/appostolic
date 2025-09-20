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
    public async Task Login_DoesNotMutate_MismatchedRolesBitmask_AfterLegacyConvergenceRemoval()
    {
        // NOTE: Legacy runtime convergence (adjusting bitmask to match legacy role string) has been removed.
        // This test now asserts that tampering the stored Roles value is surfaced as-is (defensive transparency)
        // and no attempt is made to "correct" it at login time.

        using var client = _factory.CreateClient();
        var email = "rolesconverge@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        const int tamperedValue = 6; // (Creator | Approver) missing TenantAdmin & Learner bits (original full set was 15 for historical "Owner")
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
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
        // The legacy string role field is no longer projected by the API; if present via deserialization it will be null.
        m.role.Should().BeNull();
        m.roles.Should().Be(tamperedValue, "runtime convergence logic has been retired");
    }
}
