using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Appostolic.Api.AuthTests; // TestAuthSeeder

namespace Appostolic.Api.Tests.Api;

/// <summary>
/// Verifies that an email present in Auth:SuperAdminEmails receives superadmin claim in issued tokens
/// while a non-allowlisted email does not gain cross-tenant abilities.
/// </summary>
public class SuperAdminAllowlistTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SuperAdminAllowlistTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Allowlisted_email_gets_superadmin_claim_and_can_filter_other_tenant()
    {
        // Allowlist is configured in WebAppFactory (kevin@example.com). Use deterministic issuance seeder.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = "kevin@example.com"; // allowlisted
        var slugA = $"tenant-a-{Guid.NewGuid():N}".Substring(0,20);
        var slugB = $"tenant-b-{Guid.NewGuid():N}".Substring(0,20);
        var (tokenA, _, tenantA) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slugA, owner: true);
        var (_, _, tenantB) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slugB, owner: true);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);

        // Create a notification in tenantB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nB = new Appostolic.Api.Domain.Notifications.Notification
        {
            Id = Guid.NewGuid(), Kind = Appostolic.Api.App.Notifications.EmailKind.Invite, ToEmail = "invitee@example.com", DataJson = "{}",
            Status = Appostolic.Api.Domain.Notifications.NotificationStatus.Sent, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, SentAt = DateTimeOffset.UtcNow, TenantId = tenantB
        };
        db.Notifications.Add(nB);
        await db.SaveChangesAsync();

        var filtered = await client.GetAsync($"/api/notifications?tenantId={tenantB}");
        filtered.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await filtered.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        list.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Non_allowlisted_email_cannot_filter_other_tenant()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var emailA = $"ownerA-{Guid.NewGuid():N}@example.com"; // not in allowlist
        var emailB = $"ownerB-{Guid.NewGuid():N}@example.com";
        var slugA = $"tenant-a-{Guid.NewGuid():N}".Substring(0,20);
        var slugB = $"tenant-b-{Guid.NewGuid():N}".Substring(0,20);
        var (tokenA, _, tenantA) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, emailA, slugA, owner: true);
        var (_, _, tenantB) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, emailB, slugB, owner: true);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);

        var filtered = await client.GetAsync($"/api/notifications?tenantId={tenantB}");
        // Non-superadmin should be forbidden when specifying another tenantId
        filtered.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
