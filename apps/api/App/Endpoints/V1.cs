using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Appostolic.Api.Infrastructure.Auth.Jwt;
using Appostolic.Api.Infrastructure.Providers;

namespace Appostolic.Api.App.Endpoints;

public static class V1
{
    public static IEndpointRouteBuilder MapV1Endpoints(this IEndpointRouteBuilder app)
    {
        var apiRoot = app.MapGroup("/api");
    // Resolve host environment reliably (the IEndpointRouteBuilder is the WebApplication after Build())
    var envService = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
    var loggerFactory = app.ServiceProvider.GetService<ILoggerFactory>();
    var configuration = app.ServiceProvider.GetRequiredService<IConfiguration>();
    var devGrantKey = configuration["Dev:GrantRolesKey"]; // When set, POST /api/dev/grant-roles requires header x-dev-grant-key
    var log = loggerFactory?.CreateLogger("Endpoints.V1");
    log?.LogInformation("[MapV1Endpoints] Environment='{Env}' IsDevelopment={IsDev}", envService.EnvironmentName, envService.IsDevelopment());
        // Anonymous auth endpoints
        // Forgot password: issue reset token
        apiRoot.MapPost("/auth/forgot-password", async (AppDbContext db, Appostolic.Api.App.Notifications.INotificationEnqueuer enqueuer, MagicRequestDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Email))
                return Results.BadRequest(new { error = "email is required" });
            var email = dto.Email.Trim();
            var exists = await db.Users.AsNoTracking().AnyAsync(u => u.Email == email);
            // Always accept to avoid user enumeration
            var now = DateTime.UtcNow;
            var token = Guid.NewGuid().ToString("N");
            var tokenHash = HashToken(token);
            var expiresAt = now.AddMinutes(30);
            var reset = new LoginToken { Id = Guid.NewGuid(), Email = email, TokenHash = tokenHash, Purpose = "pwreset", CreatedAt = now, ExpiresAt = expiresAt };
            db.LoginTokens.Add(reset);
            await db.SaveChangesAsync();
            if (exists)
            {
                await enqueuer.QueuePasswordResetAsync(email, null, token);
            }
            return Results.Accepted();
        }).AllowAnonymous();
        
        // (Logout endpoints relocated below after authorization group declaration)

        // Reset password: consume token and set new password
        apiRoot.MapPost("/auth/reset-password", async (AppDbContext db, Appostolic.Api.Application.Auth.IPasswordHasher hasher, ResetPasswordDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return Results.BadRequest(new { error = "token and newPassword are required" });
            var tokenHash = HashToken(dto.Token.Trim());
            var now = DateTime.UtcNow;
            var t = await db.LoginTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.Purpose == "pwreset");
            if (t is null || t.ExpiresAt < now || t.ConsumedAt is not null) return Results.BadRequest(new { error = "invalid or expired token" });
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == t.Email);
            if (user is null) return Results.BadRequest(new { error = "invalid token" });
            var (hash, salt, iterations) = hasher.HashPassword(dto.NewPassword);
            var updated = user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = now };
            db.Users.Update(updated);
            t.ConsumedAt = now;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).AllowAnonymous();

    // (Record moved below with other DTOs) SelectTenantDto declared with other record DTOs.
    // /// <summary>
    // /// Requests a magic sign-in link to be emailed to the user. Accepts an email and enqueues a one-time token.
    // /// </summary>
    apiRoot.MapPost("/auth/magic/request", async (AppDbContext db, Appostolic.Api.App.Notifications.INotificationEnqueuer enqueuer, MagicRequestDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Email))
                return Results.BadRequest(new { error = "email is required" });

            var email = dto.Email.Trim();
            var now = DateTime.UtcNow;
            // Basic rate-limit: max 5 requests per 15 minutes per email
            var windowStart = now.AddMinutes(-15);
            var recentCount = await db.LoginTokens.AsNoTracking()
                .Where(t => t.Email == email && t.CreatedAt >= windowStart)
                .CountAsync();
            if (recentCount >= 5)
            {
                // Too many requests - hide specifics
                return Results.Accepted();
            }

            var token = Guid.NewGuid().ToString("N");
            var tokenHash = HashToken(token);
            var expiresAt = now.AddMinutes(15);

            var loginToken = new LoginToken
            {
                Id = Guid.NewGuid(),
                Email = email,
                TokenHash = tokenHash,
                Purpose = "magic",
                CreatedAt = now,
                ExpiresAt = expiresAt
            };
            db.LoginTokens.Add(loginToken);
            await db.SaveChangesAsync();

            await enqueuer.QueueMagicLinkAsync(email, null, token);
            return Results.Accepted();
        }).AllowAnonymous();

    apiRoot.MapPost("/auth/magic/consume", async (HttpContext http, AppDbContext db, IJwtTokenService jwt, IRefreshTokenService refreshSvc, MagicConsumeDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
                return Results.BadRequest(new { error = "token is required" });

            var tokenHash = HashToken(dto.Token.Trim());
            var now = DateTime.UtcNow;
            var t = await db.LoginTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.Purpose == "magic");
            if (t is null) return Results.BadRequest(new { error = "invalid or expired token" });
            if (t.ExpiresAt < now) return Results.BadRequest(new { error = "invalid or expired token" });
            if (t.ConsumedAt is not null) return Results.BadRequest(new { error = "token already used" });

            // Ensure user exists; if not, create a new user and personal tenant
            var email = t.Email.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    CreatedAt = now
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();

                // Create personal tenant and membership
                var local = email.Split('@')[0].ToLowerInvariant();
                var baseSlug = $"{local}-personal";
                var slug = baseSlug;
                var attempt = 1;
                while (await db.Tenants.AsNoTracking().AnyAsync(x => x.Name == slug))
                {
                    attempt++;
                    slug = $"{baseSlug}-{attempt}";
                }
                var tenant = new Tenant { Id = Guid.NewGuid(), Name = slug, CreatedAt = now };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();

                // Respect RLS: set tenant context when inserting membership for relational providers
                // Use capability helper to decide on explicit transaction + tenant context set_config
                if (!db.Database.SupportsExplicitTransactions())
                {
                    var membership = new Membership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        UserId = user.Id,
                        // Legacy Role property removed (Story 4 Phase 2). Default flags derived from historical Owner mapping.
                        Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                        Status = MembershipStatus.Active,
                        CreatedAt = now
                    };
                    db.Memberships.Add(membership);
                    await db.SaveChangesAsync();
                }
                else
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenant.Id.ToString());
                    var membership = new Membership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        UserId = user.Id,
                        Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                        Status = MembershipStatus.Active,
                        CreatedAt = now
                    };
                    db.Memberships.Add(membership);
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
            }

            // Mark token as consumed
            t.ConsumedAt = now;
            await db.SaveChangesAsync();

            // Build memberships projection
            var memberships = await db.Memberships.AsNoTracking()
                .Where(m => m.UserId == user!.Id)
                .Join(db.Tenants.AsNoTracking(), m => m.TenantId, tn => tn.Id, (m, tn) => new
                {
                    tenantId = tn.Id,
                    tenantSlug = tn.Name,
                    roles = (int)m.Roles
                })
                .ToListAsync();

            // Legacy mode? (?includeLegacy=true)
            var includeLegacy = http.Request.Query.TryGetValue("includeLegacy", out var legacyVals) && string.Equals(legacyVals.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            if (includeLegacy)
            {
                return Results.Ok(new { user = new { user.Id, user.Email }, memberships });
            }

            // Neutral token pair
            var neutralAccess = jwt.IssueNeutralToken(user.Id.ToString(), user.TokenVersion, user.Email);
            var refreshTtlDays = int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__REFRESH_TTL_DAYS"), out var d) ? d : 30;
            var (refreshId, refreshToken, refreshExpires) = await refreshSvc.IssueNeutralAsync(user.Id, refreshTtlDays);
            var accessExpires = DateTime.UtcNow.AddMinutes(int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__ACCESS_TTL_MINUTES"), out var m) ? m : 15);

            object? tenantToken = null;
            var tenantParam = http.Request.Query.TryGetValue("tenant", out var tenantVals) ? tenantVals.ToString() : null;
            var singleMembership = memberships.Count == 1;
            if (!string.IsNullOrWhiteSpace(tenantParam) && tenantParam.Equals("auto", StringComparison.OrdinalIgnoreCase) && memberships.Count > 1)
            {
                return Results.StatusCode(StatusCodes.Status409Conflict);
            }

            Guid? targetTenantId = null;
            int rolesBitmask = 0;
            string tenantSlug = string.Empty;
            if (singleMembership)
            {
                targetTenantId = memberships[0].tenantId;
                tenantSlug = memberships[0].tenantSlug;
                rolesBitmask = memberships[0].roles;
            }
            else if (!string.IsNullOrWhiteSpace(tenantParam))
            {
                var match = memberships.FirstOrDefault(m => string.Equals(m.tenantSlug, tenantParam, StringComparison.OrdinalIgnoreCase) || m.tenantId.ToString() == tenantParam);
                if (match != null)
                {
                    targetTenantId = match.tenantId;
                    tenantSlug = match.tenantSlug;
                    rolesBitmask = match.roles;
                }
            }
            if (targetTenantId.HasValue)
            {
                var tenantAccess = jwt.IssueTenantToken(user.Id.ToString(), targetTenantId.Value, tenantSlug, rolesBitmask, user.TokenVersion, user.Email);
                tenantToken = new
                {
                    access = new { token = tenantAccess, expiresAt = accessExpires, type = "tenant", tenantId = targetTenantId.Value, tenantSlug }
                };
            }

            // Optionally set httpOnly refresh cookie (Story 4) when flag enabled.
            // Read from IConfiguration first (test host injects via InMemory collection) then fall back to environment variable.
            var refreshCookieEnabled = http.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("AUTH__REFRESH_COOKIE_ENABLED") ||
                string.Equals(Environment.GetEnvironmentVariable("AUTH__REFRESH_COOKIE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
            if (refreshCookieEnabled)
            {
                IssueRefreshCookie(http, refreshToken, refreshExpires);
            }
            // Story 2: Plaintext refresh retirement – always omit plaintext token from JSON.
            // Clients must rely on httpOnly cookie 'rt'. Former transitional flag AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT removed.
            // Metrics: count suppression to validate no legacy dependency attempts.
            var refreshObj = new { expiresAt = refreshExpires, type = "neutral" };
            Appostolic.Api.Application.Auth.AuthMetrics.IncrementPlaintextSuppressed(user.Id);

            return Results.Ok(new
            {
                user = new { user.Id, user.Email },
                memberships,
                access = new { token = neutralAccess, expiresAt = accessExpires, type = "neutral" },
                refresh = refreshObj,
                tenantToken
            });
        }).AllowAnonymous();

        // (dev grant-roles endpoint moved near method end to ensure mapping executes during startup, not inside another endpoint lambda)
        apiRoot.MapPost("/auth/signup", async (AppDbContext db, Appostolic.Api.Application.Auth.IPasswordHasher hasher, SignupDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                return Results.BadRequest(new { error = "email and password are required" });

            var email = dto.Email.Trim();
            var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            if (existing is not null) return Results.Conflict(new { error = "email already exists" });

            var (hash, salt, iterations) = hasher.HashPassword(dto.Password);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordUpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Handle invite path vs. self-serve personal tenant
            Guid tenantId;
            Roles flags;
            if (!string.IsNullOrWhiteSpace(dto.InviteToken))
            {
                // Load tracked invitation to mark as accepted
                var invite = await db.Invitations.FirstOrDefaultAsync(i => i.Token == dto.InviteToken);
                if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
                    return Results.BadRequest(new { error = "invalid or expired invite" });
                tenantId = invite.TenantId;
                flags = invite.Roles;
                // Mark as accepted; save within the same transaction later
                invite.AcceptedAt = DateTime.UtcNow;
            }
            else
            {
                // Create a personal tenant for this user.
                // Base slug: {localpart}-personal (lowercased). If taken, append a numeric suffix to ensure uniqueness.
                var local = email.Split('@')[0].ToLowerInvariant();
                var baseSlug = $"{local}-personal";
                var slug = baseSlug;
                var attempt = 1;
                while (await db.Tenants.AsNoTracking().AnyAsync(t => t.Name == slug))
                {
                    attempt++;
                    slug = $"{baseSlug}-{attempt}";
                }

                var tenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Name = slug,
                    CreatedAt = DateTime.UtcNow
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();

                tenantId = tenant.Id;
                flags = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner;
            }

            // Create membership under RLS by setting tenant context within a transaction
            var membership = new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Roles = flags,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            // Use capability helper: skip explicit transaction & tenant context when provider lacks support.
            if (!db.Database.SupportsExplicitTransactions())
            {
                db.Memberships.Add(membership);
                await db.SaveChangesAsync();
                // Save invite acceptance if applicable
                if (!string.IsNullOrWhiteSpace(dto.InviteToken))
                {
                    await db.SaveChangesAsync();
                }
            }
            else
            {
                await using (var tx = await db.Database.BeginTransactionAsync())
                {
                    await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
                    db.Memberships.Add(membership);
                    await db.SaveChangesAsync();
                    // Save invite acceptance if applicable
                    if (!string.IsNullOrWhiteSpace(dto.InviteToken))
                    {
                        await db.SaveChangesAsync();
                    }
                    await tx.CommitAsync();
                }
            }

            var tenantEntity = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
            return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Email, tenant = tenantEntity is null ? null : new { tenantEntity.Id, tenantEntity.Name } });
    }).AllowAnonymous();

        // POST /api/auth/login (AllowAnonymous)
        // Story 2: returns neutral access+refresh token pair + memberships; optional tenant access token when single membership or tenant explicitly requested.
    apiRoot.MapPost("/auth/login", async (HttpContext http, AppDbContext db, Appostolic.Api.Application.Auth.IPasswordHasher hasher, IJwtTokenService jwt, IRefreshTokenService refreshSvc, LoginDto dto) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string? failureReason = null;
            if (dto is null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            {
                failureReason = "missing_fields";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementLoginFailure(failureReason);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordLoginDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.BadRequest(new { error = "email and password are required" });
            }

            var email = dto.Email.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            // Regression fix (Story 6 follow-up): previously login endpoint auto-provisioned users / reset passwords when
            // AUTH__TEST_HELPERS_ENABLED was true. This caused invalid credential tests to succeed with 200 instead of 401
            // by silently creating or mutating users. We now enforce strict semantics: unknown user OR missing password hash
            // returns 401. Tests needing auto-provision should use explicit signup or dedicated test helper endpoints.
            if (user is null || user.PasswordHash is null || user.PasswordSalt is null)
            {
                failureReason = "unknown_user";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementLoginFailure(failureReason);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordLoginDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.Unauthorized();
            }

            var ok = hasher.Verify(dto.Password, user.PasswordHash!, user.PasswordSalt!, 0);
            if (!ok)
            {
                failureReason = "invalid_credentials";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementLoginFailure(failureReason, user.Id);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordLoginDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.Unauthorized();
            }

            var rawMemberships = await db.Memberships.AsNoTracking().Where(m => m.UserId == user.Id).ToListAsync();
            var tenantIds = rawMemberships.Select(r => r.TenantId).Distinct().ToList();
            var tenantLookup = await db.Tenants.AsNoTracking()
                .Where(t => tenantIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);
            var memberships = rawMemberships.Select(m => new
            {
                tenantId = m.TenantId,
                tenantSlug = tenantLookup.TryGetValue(m.TenantId, out var name) ? name : string.Empty,
                roles = (int)m.Roles
            }).ToList();

            // Legacy mode toggle ?includeLegacy=true
            var includeLegacy = http.Request.Query.TryGetValue("includeLegacy", out var legacyVals) && string.Equals(legacyVals.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            if (includeLegacy)
            {
                return Results.Ok(new { user.Id, user.Email, memberships });
            }

            // Issue neutral tokens
            // Superadmin elevation (Story 2 Phase A follow-up): if user email appears in allowlist configuration
            // we inject a superadmin claim into all issued tokens (neutral + optional tenant) using existing
            // extra-claims overloads. This replaces reliance on the test mint endpoint for superadmin scenarios
            // so integration tests exercise only production auth flows.
            var config = http.RequestServices.GetRequiredService<IConfiguration>();
            var allowlistRaw = config["Auth:SuperAdminEmails"] ?? string.Empty; // comma/space/semicolon separated
            bool isSuperAdmin = false;
            List<System.Security.Claims.Claim>? extraClaims = null;
            if (!string.IsNullOrWhiteSpace(allowlistRaw))
            {
                var parts = allowlistRaw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Any(p => string.Equals(p, user.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    isSuperAdmin = true;
                    extraClaims = new List<System.Security.Claims.Claim> { new("superadmin", "true") };
                }
            }

            var accessToken = isSuperAdmin
                ? jwt.IssueNeutralToken(user.Id.ToString(), user.TokenVersion, user.Email, extraClaims!)
                : jwt.IssueNeutralToken(user.Id.ToString(), user.TokenVersion, user.Email);
            var refreshTtlDays = int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__REFRESH_TTL_DAYS"), out var d) ? d : 30;
            var (refreshId, refreshToken, refreshExpires) = await refreshSvc.IssueNeutralAsync(user.Id, refreshTtlDays);
            var accessExpires = DateTime.UtcNow.AddMinutes(int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__ACCESS_TTL_MINUTES"), out var m) ? m : 15);

            object? tenantToken = null;
            var tenantParam = http.Request.Query.TryGetValue("tenant", out var tenantVals) ? tenantVals.ToString() : null;
            var singleMembership = memberships.Count == 1;
            if (!string.IsNullOrWhiteSpace(tenantParam) && tenantParam.Equals("auto", StringComparison.OrdinalIgnoreCase) && memberships.Count > 1)
            {
                return Results.StatusCode(StatusCodes.Status409Conflict);
            }

            Guid? targetTenantId = null;
            int rolesBitmask = 0;
            string tenantSlug = string.Empty;
            if (singleMembership)
            {
                targetTenantId = memberships[0].tenantId;
                tenantSlug = memberships[0].tenantSlug;
                rolesBitmask = memberships[0].roles;
            }
            else if (!string.IsNullOrWhiteSpace(tenantParam))
            {
                // Match by slug or id
                var match = memberships.FirstOrDefault(m => string.Equals(m.tenantSlug, tenantParam, StringComparison.OrdinalIgnoreCase) || m.tenantId.ToString() == tenantParam);
                if (match != null)
                {
                    targetTenantId = match.tenantId;
                    tenantSlug = match.tenantSlug;
                    rolesBitmask = match.roles;
                }
            }
            if (targetTenantId.HasValue)
            {
                var tenantAccess = isSuperAdmin
                    ? jwt.IssueTenantToken(user.Id.ToString(), targetTenantId.Value, tenantSlug, rolesBitmask, user.TokenVersion, user.Email, extraClaims!)
                    : jwt.IssueTenantToken(user.Id.ToString(), targetTenantId.Value, tenantSlug, rolesBitmask, user.TokenVersion, user.Email);
                tenantToken = new
                {
                    access = new { token = tenantAccess, expiresAt = accessExpires, type = "tenant", tenantId = targetTenantId.Value, tenantSlug }
                };
            }

            // Story 4 / Story 2 integration: cookie issuance retained; plaintext token permanently omitted (Story 2).
            var refreshCookieEnabled = http.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("AUTH__REFRESH_COOKIE_ENABLED") ||
                string.Equals(Environment.GetEnvironmentVariable("AUTH__REFRESH_COOKIE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
            if (refreshCookieEnabled)
            {
                IssueRefreshCookie(http, refreshToken, refreshExpires);
            }
            // Story 2 (original): Plaintext retired. Tests (and transitional legacy clients) still
            // expect refresh.token when the explicit exposure flag is enabled. We gate inclusion of
            // the plaintext here on AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT (default true in tests via
            // WebAppFactory). When disabled we preserve suppression metrics and omit the token.
            // Security: default exposure is false unless explicitly enabled via configuration.
            var exposePlaintext = http.RequestServices.GetRequiredService<IConfiguration>()
                .GetValue<bool>("AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", false);
            object refreshObj;
            if (exposePlaintext)
            {
                refreshObj = new { token = refreshToken, expiresAt = refreshExpires, type = "neutral" };
            }
            else
            {
                refreshObj = new { expiresAt = refreshExpires, type = "neutral" };
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementPlaintextSuppressed(user.Id);
            }

            var resultPayload = new
            {
                // Back-compat: tests (and possibly legacy clients) still expect top-level Id & Email fields.
                // New structured shape nests them under user as well. This dual shape is transitional.
                Id = user.Id,
                Email = user.Email,
                user = new { user.Id, user.Email },
                memberships,
                access = new { token = accessToken, expiresAt = accessExpires, type = "neutral" },
                refresh = refreshObj,
                tenantToken
            };
            Appostolic.Api.Application.Auth.AuthMetrics.IncrementLoginSuccess(user.Id, memberships.Count);
            sw.Stop();
            Appostolic.Api.Application.Auth.AuthMetrics.RecordLoginDuration(sw.Elapsed.TotalMilliseconds, true);
            return Results.Ok(resultPayload);
        }).AllowAnonymous();

        // POST /api/auth/select-tenant  (Story 3)
        // Purpose: Rotate a neutral refresh token and issue a tenant-scoped access token.
        // Body: { tenant: string (slug or id), refreshToken: string }
        // Success: 200 { access{token,expiresAt,type,tenantId,tenantSlug}, refresh{token,expiresAt,type} }
        // Errors:
        //   400 - missing fields
        //   401 - refresh token invalid/expired/revoked
        //   403 - user not a member of requested tenant
    apiRoot.MapPost("/auth/select-tenant", async (HttpContext http, AppDbContext db, IJwtTokenService jwt, IRefreshTokenService refreshSvc, SelectTenantDto body) =>
        {
            // Story 2 follow-up: plaintext refresh token removed from login/select responses.
            // To preserve UX we now accept the neutral refresh token via httpOnly cookie 'rt'.
            // Body.RefreshToken remains for backwards compatibility (grace period in earlier stories).
            // Validation: tenant is required; refresh token may be supplied via body or cookie.
            if (body is null || string.IsNullOrWhiteSpace(body.Tenant))
                return Results.BadRequest(new { error = "tenant is required" });

            var suppliedRefresh = body.RefreshToken;
            if (string.IsNullOrWhiteSpace(suppliedRefresh))
            {
                // Fallback to cookie when body token omitted (new invariant after plaintext retirement)
                if (!http.Request.Cookies.TryGetValue("rt", out suppliedRefresh) || string.IsNullOrWhiteSpace(suppliedRefresh))
                {
                    return Results.BadRequest(new { error = "refresh token missing" });
                }
            }

            // Refresh tokens hashed via centralized helper (Base64(SHA256(UTF8))).
            var hash = RefreshTokenHashing.Hash(suppliedRefresh.Trim());
            var now = DateTime.UtcNow;
            var rt = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(r => r.TokenHash == hash && r.Purpose == "neutral");
            if (rt is null || rt.RevokedAt.HasValue || rt.ExpiresAt <= now)
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == rt.UserId);
            if (user is null) return Results.Unauthorized();

            // Load memberships with tenant slugs in one query to avoid N+1
            var memberships = await db.Memberships.AsNoTracking()
                .Where(m => m.UserId == user.Id)
                .Join(db.Tenants.AsNoTracking(), m => m.TenantId, t => t.Id, (m, t) => new
                {
                    m.TenantId,
                    tenantSlug = t.Name,
                    roles = (int)m.Roles
                })
                .ToListAsync();
            if (memberships.Count == 0) return Results.Forbid();

            var target = memberships.FirstOrDefault(m => string.Equals(m.tenantSlug, body.Tenant, StringComparison.OrdinalIgnoreCase) || m.TenantId.ToString() == body.Tenant);
            if (target is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Rotate refresh (revoke old + issue new neutral) BEFORE issuing new access token
            await refreshSvc.RevokeAsync(rt.Id);
            var refreshTtlDays = int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__REFRESH_TTL_DAYS"), out var d) ? d : 30;
            var (newRefreshId, newRefresh, newRefreshExpires) = await refreshSvc.IssueNeutralAsync(user.Id, refreshTtlDays);
            var accessExpires = DateTime.UtcNow.AddMinutes(int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__ACCESS_TTL_MINUTES"), out var m) ? m : 15);

            // Superadmin allowlist claim injection (mirrors login endpoint logic)
            var config = http.RequestServices.GetRequiredService<IConfiguration>();
            var allowlistRaw = config["Auth:SuperAdminEmails"] ?? string.Empty;
            List<System.Security.Claims.Claim>? extraClaims = null;
            if (!string.IsNullOrWhiteSpace(allowlistRaw))
            {
                var parts = allowlistRaw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Any(p => string.Equals(p, user.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    extraClaims = new List<System.Security.Claims.Claim> { new("superadmin", "true") };
                }
            }
            var tenantAccess = extraClaims is not null
                ? jwt.IssueTenantToken(user.Id.ToString(), target.TenantId, target.tenantSlug, target.roles, user.TokenVersion, user.Email, extraClaims)
                : jwt.IssueTenantToken(user.Id.ToString(), target.TenantId, target.tenantSlug, target.roles, user.TokenVersion, user.Email);
            // Story 4: Rotate cookie as well (overwrite with new refresh) if enabled
            var refreshCookieEnabled = http.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("AUTH__REFRESH_COOKIE_ENABLED") ||
                string.Equals(Environment.GetEnvironmentVariable("AUTH__REFRESH_COOKIE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
            if (refreshCookieEnabled)
            {
                IssueRefreshCookie(http, newRefresh, newRefreshExpires);
            }
            // Story 2 (original): Plaintext retired. Transitional flag AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT
            // allows tests to continue asserting rotation semantics using the raw token while the
            // application migrates UI flows to cookie-only. When disabled, we increment suppression metric.
            var exposePlaintextSelect = http.RequestServices.GetRequiredService<IConfiguration>()
                .GetValue<bool>("AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", false);
            object refreshObj;
            if (exposePlaintextSelect)
            {
                refreshObj = new { token = newRefresh, expiresAt = newRefreshExpires, type = "neutral" };
            }
            else
            {
                refreshObj = new { expiresAt = newRefreshExpires, type = "neutral" };
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementPlaintextSuppressed(user.Id);
            }

            return Results.Ok(new
            {
                access = new { token = tenantAccess, expiresAt = accessExpires, type = "tenant", tenantId = target.TenantId, tenantSlug = target.tenantSlug },
                refresh = refreshObj
            });
        }).AllowAnonymous();

        var api = apiRoot.RequireAuthorization();
        // --- Story 7: Logout Endpoints (authenticated) ---
        // POST /api/auth/logout - revoke a single provided neutral refresh token (cookie 'rt' or body refreshToken during grace)
        api.MapPost("/auth/logout", async (HttpContext http, ClaimsPrincipal principal, AppDbContext db, IRefreshTokenService refreshSvc) =>
        {
            var config = http.RequestServices.GetRequiredService<IConfiguration>();
            var graceEnabled = config.GetValue<bool>("AUTH__REFRESH_JSON_GRACE_ENABLED", true);
            var userIdStr = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.Unauthorized();
            string? bodyToken = null;
            if (graceEnabled && http.Request.ContentLength is > 0 && http.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    using var doc = await JsonDocument.ParseAsync(http.Request.Body);
                    if (doc.RootElement.TryGetProperty("refreshToken", out var rtProp) && rtProp.ValueKind == JsonValueKind.String)
                        bodyToken = rtProp.GetString();
                    else
                        bodyToken = string.Empty;
                }
                catch { bodyToken = string.Empty; }
            }
            var cookieToken = http.Request.Cookies.TryGetValue("rt", out var ct) ? ct : null;
            var bodyProvided = bodyToken is not null;
            string? supplied = null;
            if (bodyProvided)
            {
                supplied = bodyToken;
            }
            else
            {
                supplied = cookieToken ?? bodyToken;
            }
            if (string.IsNullOrWhiteSpace(supplied))
            {
                return Results.BadRequest(new { code = "missing_refresh" });
            }
            var hash = RefreshTokenHashing.Hash(supplied.Trim());
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash && r.Purpose == "neutral");
            if (rt != null && rt.UserId == userId && !rt.RevokedAt.HasValue)
                await refreshSvc.RevokeAsync(rt.Id);
            if (cookieToken != null)
            {
                http.Response.Cookies.Append("rt", string.Empty, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(-1)
                });
            }
            var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth.Logout");
            logger.LogInformation("auth.logout.single user={UserId} tokenFound={TokenFound}", userId, rt != null);
            var act1 = System.Diagnostics.Activity.Current;
            act1?.SetTag("auth.user_id", userId);
            act1?.SetTag("auth.event", "logout.single");
            act1?.SetTag("auth.token_found", rt != null);
            Appostolic.Api.Application.Auth.AuthMetrics.IncrementLogoutSingle(userId, rt != null);
            return Results.NoContent();
        });

        // POST /api/auth/logout/all - revoke all active neutral refresh tokens and bump token version (invalidating current access tokens)
        api.MapPost("/auth/logout/all", async (HttpContext http, ClaimsPrincipal principal, AppDbContext db, IRefreshTokenService refreshSvc) =>
        {
            // Support tokens where the handler mapped 'sub' to NameIdentifier
            var userIdStr = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.Unauthorized();
            var count = await refreshSvc.RevokeAllForUserAsync(userId);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                // If the user disappeared concurrently treat as success from client perspective.
                var loggerMissing = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth.Logout");
                loggerMissing.LogWarning("auth.logout.all user_missing user={UserId}", userId);
            }
            else
            {
                // Detach the existing tracked instance then attach an updated copy (record has init-only properties)
                var existingEntry = db.Entry(user);
                existingEntry.State = EntityState.Detached;
                var bumped = user with { TokenVersion = user.TokenVersion + 1 };
                db.Users.Update(bumped);
                await db.SaveChangesAsync();
            }
            if (http.Request.Cookies.ContainsKey("rt"))
            {
                http.Response.Cookies.Append("rt", string.Empty, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(-1)
                });
            }
            var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth.Logout");
            logger.LogInformation("auth.logout.all user={UserId} revokedCount={Count}", userId, count);
            var act2 = System.Diagnostics.Activity.Current;
            act2?.SetTag("auth.user_id", userId);
            act2?.SetTag("auth.event", "logout.all");
            act2?.SetTag("auth.revoked_count", count);
            Appostolic.Api.Application.Auth.AuthMetrics.IncrementLogoutAll(userId, count);
            return Results.NoContent();
        });
        // --- Story 6: General refresh endpoint (cookie-first with transitional body support) ---
        // POST /api/auth/refresh
        // Preferred: httpOnly cookie 'rt' supplies refresh token. Transitional: JSON body { refreshToken } while grace flag enabled.
        // Returns: { user, memberships, access, refresh, tenantToken? }
        // Errors: 400 missing, 401 invalid/expired/revoked/reuse, 403 tenant membership mismatch.
        apiRoot.MapPost("/auth/refresh", async (HttpContext http, AppDbContext db, IJwtTokenService jwt, IRefreshTokenService refreshSvc, Appostolic.Api.Infrastructure.Auth.Refresh.IRefreshRateLimiter limiter, Microsoft.Extensions.Options.IOptions<Appostolic.Api.Infrastructure.Auth.Refresh.RefreshRateLimitOptions> rlOpts) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string? failureReason = null;
            var now = DateTime.UtcNow;
            var config = http.RequestServices.GetRequiredService<IConfiguration>();
            // Story 3 (refined): Perform rate limit evaluation only AFTER we know the userId (when possible)
            // to avoid double counting (previous implementation evaluated twice per request which complicated
            // threshold reasoning in tests & ops). We defer evaluation until after refresh token lookup.
            // For invalid / missing tokens we will still evaluate ip-only just before returning 401 so abuse
            // (spraying invalid tokens) is still limited.
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            Appostolic.Api.Infrastructure.Auth.Refresh.RateLimitEvaluation evaluation; // populated later (ip-only or user+ip)
            var graceEnabled = config.GetValue<bool>("AUTH__REFRESH_JSON_GRACE_ENABLED", true);
            var deprecationDate = config["AUTH__REFRESH_DEPRECATION_DATE"]; // RFC1123 date string optional
            string? bodyToken = null;
            // Attempt to read small JSON body if content-type indicates JSON and grace is enabled
            if (graceEnabled && http.Request.ContentLength is > 0 && http.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body);
                    if (doc.RootElement.TryGetProperty("refreshToken", out var rtProp) && rtProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        bodyToken = rtProp.GetString();
                    }
                }
                catch { /* swallow parse errors; treat as missing */ }
            }
            var cookieToken = http.Request.Cookies.TryGetValue("rt", out var ct) ? ct : null;
            var suppliedToken = cookieToken ?? bodyToken;
            if (string.IsNullOrWhiteSpace(suppliedToken))
            {
                failureReason = "missing_refresh";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.BadRequest(new { code = "missing_refresh" });
            }

            // Reject body token usage if grace disabled and no cookie present
            if (!graceEnabled && cookieToken is null)
            {
                failureReason = "refresh_body_disallowed";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.BadRequest(new { code = "refresh_body_disallowed" });
            }

            // Hash same way as RefreshTokenService (Base64(SHA256))
            static string HashRefresh(string t)
            {
                using var sha = SHA256.Create();
                var bytes = Encoding.UTF8.GetBytes(t);
                return Convert.ToBase64String(sha.ComputeHash(bytes));
            }
            var hash = HashRefresh(suppliedToken.Trim());
            var evalSw = System.Diagnostics.Stopwatch.StartNew();
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash && r.Purpose == "neutral");
            if (rt is null)
            {
                // Evaluate limiter ip-only for invalid token paths (prevents unlimited invalid sprays)
                evaluation = limiter.Evaluate(null, ip);
                evalSw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshLimiterEvaluation(evalSw.Elapsed.TotalMilliseconds, evaluation.IsLimited ? (evaluation.DryRun ? "dryrun_block" : "block") : "hit");
                if (evaluation.IsLimited)
                {
                    // Structured security event (v1)
                    var secLogger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Security.Auth");
                    secLogger.LogInformation("{Json}", System.Text.Json.JsonSerializer.Serialize(new {
                        v = 1,
                        ts = DateTime.UtcNow,
                        type = "refresh_rate_limited",
                        ip = ip,
                        reason = "window",
                        meta = evaluation.DryRun ? new { dry_run = true } : null
                    }));
                }
                if (evaluation.IsLimited)
                {
                    failureReason = "refresh_rate_limited";
                    Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshRateLimited();
                    Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason);
                    sw.Stop();
                    Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                    return Results.Json(new { code = "refresh_rate_limited", retryAfterSeconds = evaluation.WindowSeconds }, statusCode: StatusCodes.Status429TooManyRequests);
                }
                failureReason = "refresh_invalid";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.Json(new { code = "refresh_invalid" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // We have a valid refresh token, evaluate limiter with user dimension now.
            evaluation = limiter.Evaluate(rt.UserId, ip);
            evalSw.Stop();
            Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshLimiterEvaluation(evalSw.Elapsed.TotalMilliseconds, evaluation.IsLimited ? (evaluation.DryRun ? "dryrun_block" : "block") : "hit");
            if (evaluation.IsLimited)
            {
                // Structured security event (v1)
                var secLogger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Security.Auth");
                secLogger.LogInformation("{Json}", System.Text.Json.JsonSerializer.Serialize(new {
                    v = 1,
                    ts = DateTime.UtcNow,
                    type = "refresh_rate_limited",
                    user_id = rt.UserId,
                    refresh_id = rt.Id,
                    ip = ip,
                    reason = "window",
                    meta = evaluation.DryRun ? new { dry_run = true } : null
                }));
                failureReason = "refresh_rate_limited";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshRateLimited();
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason, rt.UserId);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.Json(new { code = "refresh_rate_limited", retryAfterSeconds = evaluation.WindowSeconds }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            if (rt.RevokedAt.HasValue)
            {
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementReuseDenied(rt.UserId, rt.Id);
                failureReason = "refresh_reuse";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason, rt.UserId);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.Json(new { code = "refresh_reuse" }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (rt.ExpiresAt <= now)
            {
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementExpired(rt.UserId, rt.Id);
                failureReason = "refresh_expired";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason, rt.UserId);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.Json(new { code = "refresh_expired" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == rt.UserId);
            if (user is null)
            {
                failureReason = "refresh_invalid";
                Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason);
                sw.Stop();
                Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                return Results.Json(new { code = "refresh_invalid" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // Load memberships (slug + roles) for response parity & optional tenant param
            var memberships = await db.Memberships.AsNoTracking()
                .Where(m => m.UserId == user.Id)
                .Join(db.Tenants.AsNoTracking(), m => m.TenantId, t => t.Id, (m, t) => new
                {
                    tenantId = m.TenantId,
                    tenantSlug = t.Name,
                    roles = (int)m.Roles
                })
                .ToListAsync();

            object? tenantToken = null;
            string? tenantParam = http.Request.Query["tenant"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tenantParam))
            {
                var match = memberships.FirstOrDefault(m => string.Equals(m.tenantSlug, tenantParam, StringComparison.OrdinalIgnoreCase) || m.tenantId.ToString() == tenantParam);
                if (match is null)
                {
                    failureReason = "refresh_forbidden_tenant";
                    Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshFailure(failureReason, user.Id);
                    sw.Stop();
                    Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, false);
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                var accessExpiresPreview = DateTime.UtcNow.AddMinutes(int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__ACCESS_TTL_MINUTES"), out var mm) ? mm : 15);
                var tenantAccess = jwt.IssueTenantToken(user.Id.ToString(), match.tenantId, match.tenantSlug, match.roles, user.TokenVersion, user.Email);
                tenantToken = new { access = new { token = tenantAccess, expiresAt = accessExpiresPreview, type = "tenant", tenantId = match.tenantId, tenantSlug = match.tenantSlug } };
            }

            // Rotate refresh (revocation + new issuance). Intentionally avoids explicit transaction when using InMemory provider
            // (transactions not supported). Production relational providers still get sequential revoke+issue which is acceptable
            // for this rotation scenario (low contention domain); future optimization can add provider-specific atomic path.
            await refreshSvc.RevokeAsync(rt.Id); // sets revoked_at & reason="rotated"
            var refreshTtlDays = int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__REFRESH_TTL_DAYS"), out var d) ? d : 30;
            var (newRefreshId, newRefresh, newRefreshExpires) = await refreshSvc.IssueNeutralAsync(user.Id, refreshTtlDays);

            var accessExpires = DateTime.UtcNow.AddMinutes(int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__ACCESS_TTL_MINUTES"), out var m) ? m : 15);
            // Existing service issues neutral access tokens via IssueAccessToken (used in login). Reuse neutral path (no tenant claims).
            var neutralAccess = jwt.IssueNeutralToken(user.Id.ToString(), user.TokenVersion, user.Email);

            // Cookie issuance if enabled
            var refreshCookieEnabled = config.GetValue<bool>("AUTH__REFRESH_COOKIE_ENABLED") ||
                string.Equals(Environment.GetEnvironmentVariable("AUTH__REFRESH_COOKIE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
            if (refreshCookieEnabled)
            {
                IssueRefreshCookie(http, newRefresh, newRefreshExpires);
            }

            // Deprecation headers if body token used and grace still enabled & date provided
            if (graceEnabled && bodyToken is not null && !string.IsNullOrWhiteSpace(deprecationDate))
            {
                http.Response.Headers["Deprecation"] = "true";
                http.Response.Headers["Sunset"] = deprecationDate!;
            }

            // Story 2: Plaintext retired – always omit token and mark suppression.
            var refreshObj = new { expiresAt = newRefreshExpires, type = "neutral" };
            Appostolic.Api.Application.Auth.AuthMetrics.IncrementPlaintextSuppressed(user.Id);
            var response = new
            {
                user = new { user.Id, user.Email },
                memberships,
                access = new { token = neutralAccess, expiresAt = accessExpires, type = "neutral" },
                refresh = refreshObj,
                tenantToken
            };

            // Basic structured log (counters deferred to Story 9)
            var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth.Refresh");
            logger.LogInformation("auth.refresh.rotate user={UserId} refreshId={RefreshId}", user.Id, rt.Id);
            var act3 = System.Diagnostics.Activity.Current;
            act3?.SetTag("auth.user_id", user.Id);
            act3?.SetTag("auth.event", "refresh.rotate");
            act3?.SetTag("auth.refresh.old_id", rt.Id);
            act3?.SetTag("auth.refresh.new_id", newRefreshId);
            Appostolic.Api.Application.Auth.AuthMetrics.IncrementRotation(user.Id, rt.Id, newRefreshId);

            // success path metrics
            Appostolic.Api.Application.Auth.AuthMetrics.IncrementRefreshSuccess(user.Id);
            Appostolic.Api.Application.Auth.AuthMetrics.RecordRefreshDuration(sw.Elapsed.TotalMilliseconds, true);
            sw.Stop();
            return Results.Ok(response);
        }).AllowAnonymous();
        // Change password (authenticated)
        api.MapPost("/auth/change-password", async (ClaimsPrincipal principal, AppDbContext db, Appostolic.Api.Application.Auth.IPasswordHasher hasher, ChangePasswordDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return Results.BadRequest(new { error = "currentPassword and newPassword are required" });
            var emailClaim = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(emailClaim)) return Results.Unauthorized();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == emailClaim);
            if (user is null || user.PasswordHash is null || user.PasswordSalt is null)
                return Results.Unauthorized();
            var ok = hasher.Verify(dto.CurrentPassword, user.PasswordHash!, user.PasswordSalt!, 0);
            if (!ok) return Results.Unauthorized();
            var (newHash, newSalt, iterations) = hasher.HashPassword(dto.NewPassword);
            var updated = user with { PasswordHash = newHash, PasswordSalt = newSalt, PasswordUpdatedAt = DateTime.UtcNow, TokenVersion = user.TokenVersion + 1 };
            db.Users.Update(updated);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // GET /api/me
        api.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var email = user.FindFirstValue("email") ?? string.Empty;
            var tenantId = user.FindFirstValue("tenant_id") ?? string.Empty;
            var tenantSlug = user.FindFirstValue("tenant_slug") ?? string.Empty;
            return Results.Ok(new { sub, email, tenant_id = tenantId, tenant_slug = tenantSlug });
        });

        // GET /api/tenants (tenant-scoped; returns current tenant)
        api.MapGet("/tenants", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId);
            return Results.Ok(t is null ? Array.Empty<object>() : new[] { new { t.Id, t.Name } });
        });

        // GET /api/lessons (tenant-scoped)
        api.MapGet("/lessons", async (int? take, int? skip, AppDbContext db) =>
        {
            int takeVal = take.GetValueOrDefault(20);
            int skipVal = skip.GetValueOrDefault(0);
            var items = await db.Lessons.AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .Skip(skipVal)
                .Take(takeVal)
                .ToListAsync();
            return Results.Ok(items);
        });

        // POST /api/lessons (tenant-scoped)
    api.MapPost("/lessons", async (ClaimsPrincipal user, AppDbContext db, NewLessonDto dto) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            var userIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(tenantIdStr, out var tenantId) || !Guid.TryParse(userIdStr, out _))
                return Results.BadRequest(new { error = "invalid user/tenant" });

            var lesson = new Lesson
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Title = string.IsNullOrWhiteSpace(dto?.Title) ? "Untitled" : dto!.Title!,
                Status = LessonStatus.Draft,
                Audience = LessonAudience.All,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Lessons.Add(lesson);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lessons/{lesson.Id}", lesson);
        }).RequireAuthorization("Creator");

        // GET /api/tenants/{tenantId}/members (Admin/Owner only)
    api.MapGet("/tenants/{tenantId:guid}/members", async (Guid tenantId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var userIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if ((me.Roles & Roles.TenantAdmin) == 0) return Results.Forbid();

            var members = await db.Memberships.AsNoTracking()
                .Where(m => m.TenantId == tenantId)
                .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new
                {
                    userId = u.Id,
                    email = u.Email,
                    roles = (int)m.Roles,
                    joinedAt = m.CreatedAt
                })
                .OrderBy(x => x.email)
                .ToListAsync();
            return Results.Ok(members);
        }).RequireAuthorization("TenantAdmin");

        // POST /api/tenants/{tenantId}/invites (Admin/Owner only)
        // Story 4 (refLeg-04): Legacy `role` field deprecated — callers MUST provide roles flags via either
        //   - roles[] (string names of flags, e.g., ["TenantAdmin","Approver","Creator","Learner"]) OR
        //   - rolesValue (int bitmask)
        // Requests including a non-null legacy `role` will be rejected with error code LEGACY_ROLE_DEPRECATED to flush
        // lingering clients before the legacy column is dropped. We still persist the legacy column value (Viewer) only
        // as inert placeholder until removal; runtime NEVER derives flags from it now.
        api.MapPost("/tenants/{tenantId:guid}/invites", async (
            Guid tenantId,
            ClaimsPrincipal user,
            AppDbContext db,
            IConfiguration config,
            InviteRequest dto) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var userIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if ((me.Roles & Roles.TenantAdmin) == 0) return Results.Forbid();

            if (dto is null || string.IsNullOrWhiteSpace(dto.Email))
                return Results.BadRequest(new { error = "email is required" });

            // Legacy single Role field fully removed (Story 4 Phase 2). Clients must send roles flags (names or bitmask).

            var email = dto.Email.Trim();
            var emailLower = email.ToLowerInvariant();

            // Parse roles from either names array OR rolesValue bitmask
            Roles rolesFlags = Roles.None;
            if (dto.Roles is not null && dto.Roles.Length > 0)
            {
                if (!TryParseRoleNames(dto.Roles, out rolesFlags, out var invalidName))
                    return Results.BadRequest(new { error = $"invalid role flag: {invalidName}" });
            }
            else if (dto.RolesValue is not null)
            {
                var candidate = (Roles)dto.RolesValue.Value;
                // Basic validation: no unknown bits (only lower 4 currently) and non-zero
                var unknown = candidate & ~(Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner);
                if (unknown != 0)
                    return Results.BadRequest(new { error = "rolesValue contains unknown bits" });
                rolesFlags = candidate;
            }

            if (rolesFlags == Roles.None)
            {
                return Results.BadRequest(new { error = "at least one role flag required", code = "NO_FLAGS" });
            }

            // If user already exists and is a member, conflict
            var existingUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            if (existingUser is not null)
            {
                var alreadyMember = await db.Memberships.AsNoTracking()
                    .AnyAsync(m => m.TenantId == tenantId && m.UserId == existingUser.Id);
                if (alreadyMember)
                    return Results.Conflict(new { error = "user already a member" });
            }

            // Upsert single invitation per (tenant, lower(email))
            var existingInvite = await db.Invitations.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Email.ToLower() == emailLower);
            var token = Guid.NewGuid().ToString("N");
            var expiresAt = DateTime.UtcNow.AddDays(7);
            if (existingInvite is null)
            {
                var invite = new Invitation
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Email = email,
                    Roles = rolesFlags,
                    Token = token,
                    ExpiresAt = expiresAt,
                    InvitedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                db.Invitations.Add(invite);
            }
            else
            {
                existingInvite.Email = email; // normalize casing
                existingInvite.Roles = rolesFlags;
                existingInvite.Token = token;
                existingInvite.ExpiresAt = expiresAt;
                existingInvite.InvitedByUserId = userId;
                existingInvite.AcceptedAt = null;
            }
            await db.SaveChangesAsync();

            // Send dev email via SMTP (Mailhog) with a simple HTML body
            try
            {
                var host = config["Smtp:Host"] ?? "localhost";
                var portStr = config["Smtp:Port"] ?? "1025";
                var from = config["Smtp:From"] ?? "no-reply@appostolic.local";
                _ = int.TryParse(portStr, out var port);
                using var client = new SmtpClient(host, port == 0 ? 1025 : port);
                using var msg = new MailMessage();
                msg.From = new MailAddress(from);
                msg.To.Add(new MailAddress(email));
                msg.Subject = $"You're invited to join tenant {user.FindFirstValue("tenant_slug") ?? tenantId.ToString()}";
                var signupUrl = $"http://localhost:3000/invite/accept?token={token}";
                msg.IsBodyHtml = true;
                var roleFlagsLabel = rolesFlags.ToString();
                msg.Body = $"<p>Hello,</p><p>You were invited to join <b>{user.FindFirstValue("tenant_slug") ?? tenantId.ToString()}</b> with roles <b>{roleFlagsLabel}</b>.</p><p>To proceed, open this link: <a href='{signupUrl}'>Accept invite</a>.</p><p>If you already have an account, you’ll be asked to sign in first. After signing in, your invite will be applied automatically.</p><p>This invite expires at {expiresAt:u}.</p>";
                await client.SendMailAsync(msg);
            }
            catch
            {
                // Best-effort in dev; ignore email failures
            }

            return Results.Created($"/api/tenants/{tenantId}/invites/{email}", new { email, roles = rolesFlags.ToString(), rolesValue = (int)rolesFlags, expiresAt });
        }).RequireAuthorization("TenantAdmin");

        // GET /api/tenants/{tenantId}/invites (Admin/Owner only)
    api.MapGet("/tenants/{tenantId:guid}/invites", async (Guid tenantId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var userIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            // Flags-only authorization (legacy Role removed): require caller to have TenantAdmin flag
            if ((me.Roles & Roles.TenantAdmin) == 0) return Results.Forbid();

            var invites = await db.Invitations.AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new
                {
                    email = i.Email,
                    roles = i.Roles.ToString(),
                    rolesValue = (int)i.Roles,
                    expiresAt = i.ExpiresAt,
                    acceptedAt = i.AcceptedAt,
                    invitedByEmail = db.Users.AsNoTracking().Where(u => u.Id == i.InvitedByUserId).Select(u => u.Email).FirstOrDefault(),
                    acceptedByEmail = db.Users.AsNoTracking().Where(u => u.Email == i.Email).Select(u => u.Email).FirstOrDefault()
                })
                .ToListAsync();
            return Results.Ok(invites);
        }).RequireAuthorization("TenantAdmin");

        // POST /api/tenants/{tenantId}/invites/{email}/resend (Admin/Owner only)
        api.MapPost("/tenants/{tenantId:guid}/invites/{email}/resend", async (
            Guid tenantId,
            string email,
            ClaimsPrincipal user,
            AppDbContext db,
            IConfiguration config) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var userIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if ((me.Roles & Roles.TenantAdmin) == 0) return Results.Forbid();

            var lower = email.Trim().ToLowerInvariant();
            var invite = await db.Invitations.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Email.ToLower() == lower);
            if (invite is null) return Results.NotFound();

            invite.Token = Guid.NewGuid().ToString("N");
            invite.ExpiresAt = DateTime.UtcNow.AddDays(7);
            invite.InvitedByUserId = userId;
            invite.AcceptedAt = null;
            await db.SaveChangesAsync();

            try
            {
                var host = config["Smtp:Host"] ?? "localhost";
                var portStr = config["Smtp:Port"] ?? "1025";
                var from = config["Smtp:From"] ?? "no-reply@appostolic.local";
                _ = int.TryParse(portStr, out var port);
                using var client = new SmtpClient(host, port == 0 ? 1025 : port);
                using var msg = new MailMessage();
                msg.From = new MailAddress(from);
                msg.To.Add(new MailAddress(invite.Email));
                var tenantSlug = user.FindFirstValue("tenant_slug") ?? tenantId.ToString();
                msg.Subject = $"Your invite was re-sent for {tenantSlug}";
                var signupUrl = $"http://localhost:3000/invite/accept?token={invite.Token}";
                msg.IsBodyHtml = true;
                msg.Body = $"<p>Hello,</p><p>You were invited to join <b>{tenantSlug}</b> with roles <b>{invite.Roles}</b>.</p><p>To proceed, open this link: <a href='{signupUrl}'>Accept invite</a>.</p><p>If you already have an account, you’ll be asked to sign in first. After signing in, your invite will be applied automatically.</p><p>This invite expires at {invite.ExpiresAt:u}.</p>";
                await client.SendMailAsync(msg);
            }
            catch { }

            return Results.Ok(new { email = invite.Email, roles = invite.Roles.ToString(), rolesValue = (int)invite.Roles, expiresAt = invite.ExpiresAt });
        }).RequireAuthorization("TenantAdmin");

        // DELETE /api/tenants/{tenantId}/invites/{email} (Admin/Owner only)
        api.MapDelete("/tenants/{tenantId:guid}/invites/{email}", async (
            Guid tenantId,
            string email,
            ClaimsPrincipal user,
            AppDbContext db) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();
            // Align with GET invites endpoint: fallback to NameIdentifier if 'sub' absent (tokens issued by password flow)
            var userIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if ((me.Roles & Roles.TenantAdmin) == 0) return Results.Forbid();

            var lower = email.Trim().ToLowerInvariant();
            var invite = await db.Invitations.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Email.ToLower() == lower);
            if (invite is null) return Results.NotFound();

            db.Invitations.Remove(invite);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("TenantAdmin");

        // PUT /api/tenants/{tenantId}/members/{userId} — change member role (DEPRECATED: legacy single-role path)
        api.MapPut("/tenants/{tenantId:guid}/members/{userId:guid}", (
            Guid tenantId,
            Guid userId,
            ClaimsPrincipal user,
            AppDbContext db) =>
        {
            // Story 4 Phase 2: This legacy single-role mutation endpoint is deprecated.
            // All clients must use the flags endpoint: POST /api/tenants/{tenantId}/memberships/{userId}/roles { roles: [..] }
            // Removed unnecessary async (no awaits) to resolve CS1998 warning.
            return Results.BadRequest(new { error = "legacy member role change endpoint deprecated; use roles flags endpoint", code = "LEGACY_ROLE_DEPRECATED" });
        }).RequireAuthorization("TenantAdmin");

        // DELETE /api/tenants/{tenantId}/members/{userId} — remove member (Admin/Owner only)
        api.MapDelete("/tenants/{tenantId:guid}/members/{userId:guid}", async (
            Guid tenantId,
            Guid userId,
            ClaimsPrincipal user,
            AppDbContext db) =>
        {
            var callerIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(callerIdStr, out var callerId)) return Results.Unauthorized();
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == callerId);
            if (me is null) return Results.Forbid();
            if ((me.Roles & Roles.TenantAdmin) == 0) return Results.Forbid();

            var target = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
            if (target is null) return Results.NotFound();

            // Story 1.4 invariant: prevent removing the last TenantAdmin (Owner/Admin)
            var adminCount = await db.Memberships.AsNoTracking()
                .CountAsync(m => m.TenantId == tenantId && (m.Roles & Roles.TenantAdmin) != 0 && m.Status == MembershipStatus.Active);
            if ((target.Roles & Roles.TenantAdmin) != 0 && adminCount <= 1)
            {
                return Results.Conflict(new { error = "cannot remove the last TenantAdmin" });
            }

            // Forbid removing yourself (only applies when not the last TenantAdmin)
            if (target.UserId == callerId)
                return Results.BadRequest(new { error = "cannot remove yourself" });

            if (!db.Database.SupportsExplicitTransactions())
            {
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }

            if (db.Database.CurrentTransaction is not null)
            {
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
            db.Memberships.Remove(target);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization("TenantAdmin");

        // GET /api/tenants/{tenantId}/memberships — List memberships with roles flags (TenantAdmin only)
        // Purpose: Admin surface to review all tenant memberships including both legacy Role and new Roles flags.
        // Auth: Requires TenantAdmin for the current tenant. Additionally validates that the caller's tenant_id claim matches the route tenantId.
        // Inputs: route tenantId (Guid)
        // Output: 200 OK with array of { userId, email, role (legacy), roles (flags string), rolesValue (int), joinedAt } sorted by email.
        // Errors: 400 when the caller's tenant claim is invalid; 403 when claim tenant differs from route; 401 handled by auth middleware.
        api.MapGet("/tenants/{tenantId:guid}/memberships", async (Guid tenantId, ClaimsPrincipal user, AppDbContext db) =>
        {
            // Validate tenant claim vs route for defense-in-depth even though policy and middleware scope the tenant context.
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var list = await db.Memberships.AsNoTracking()
                .Where(m => m.TenantId == tenantId)
                .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new
                {
                    userId = u.Id,
                    email = u.Email,
                    roles = m.Roles.ToString(),
                    rolesValue = (int)m.Roles,
                    status = m.Status.ToString(),
                    joinedAt = m.CreatedAt
                })
                .OrderBy(x => x.email)
                .ToListAsync();
            return Results.Ok(list);
        }).RequireAuthorization("TenantAdmin");

        // PUT /api/tenants/{tenantId}/members/{userId}/status — toggle Active status (Admin/Owner only)
        api.MapPut("/tenants/{tenantId:guid}/members/{userId:guid}/status", async (
            Guid tenantId,
            Guid userId,
            ClaimsPrincipal user,
            AppDbContext db,
            UpdateMemberStatusDto dto) =>
        {
            var callerIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(callerIdStr, out var callerId)) return Results.Unauthorized();
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == callerId);
            if (me is null) return Results.Forbid();
            if ((me.Roles & Roles.TenantAdmin) == 0) return Results.Forbid();

            var target = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
            if (target is null) return Results.NotFound();

            var newStatus = dto.Active ? MembershipStatus.Active : MembershipStatus.Suspended;
            if (target.Status == newStatus) return Results.NoContent();

            // Enforce invariant: cannot deactivate the last TenantAdmin (hybrid definition).
            if (!dto.Active)
            {
                bool targetIsTenantAdmin = (target.Roles & Roles.TenantAdmin) != 0;
                if (targetIsTenantAdmin)
                {
                    var otherActiveAdmins = await db.Memberships.AsNoTracking()
                        .Where(m => m.TenantId == tenantId && m.UserId != target.UserId && m.Status == MembershipStatus.Active)
                        .CountAsync(m => (m.Roles & Roles.TenantAdmin) != 0);
                    if (otherActiveAdmins <= 0)
                    {
                        return Results.Conflict(new { error = "cannot deactivate the last TenantAdmin" });
                    }
                }
            }

            if (!db.Database.SupportsExplicitTransactions())
            {
                var replacement = new Membership
                {
                    Id = target.Id,
                    TenantId = target.TenantId,
                    UserId = target.UserId,
                    Roles = target.Roles,
                    Status = newStatus,
                    CreatedAt = target.CreatedAt
                };
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                db.Memberships.Add(replacement);
                await db.SaveChangesAsync();
                return Results.Ok(new { userId = replacement.UserId, status = replacement.Status.ToString() });
            }
            if (db.Database.CurrentTransaction is not null)
            {
                var replacement = new Membership
                {
                    Id = target.Id,
                    TenantId = target.TenantId,
                    UserId = target.UserId,
                    Roles = target.Roles,
                    Status = newStatus,
                    CreatedAt = target.CreatedAt
                };
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                db.Memberships.Add(replacement);
                await db.SaveChangesAsync();
                return Results.Ok(new { userId = replacement.UserId, status = replacement.Status.ToString() });
            }

            await using var tx2 = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
            var replacement2 = new Membership
            {
                Id = target.Id,
                TenantId = target.TenantId,
                UserId = target.UserId,
                Roles = target.Roles,
                Status = newStatus,
                CreatedAt = target.CreatedAt
            };
            db.Memberships.Remove(target);
            await db.SaveChangesAsync();
            db.Memberships.Add(replacement2);
            await db.SaveChangesAsync();
            await tx2.CommitAsync();
            return Results.Ok(new { userId = replacement2.UserId, status = replacement2.Status.ToString() });
        }).RequireAuthorization("TenantAdmin");

        // GET /api/tenants/{tenantId}/audits — List recent audit entries for membership role changes (TenantAdmin only)
        // Purpose: Allow admins to review who changed which roles and when for this tenant.
        // Auth: Requires TenantAdmin for the current tenant. Validates route tenantId matches caller's tenant_id claim.
        // Inputs: route tenantId (Guid)
        // Optional query:
        //   - take (default 50, max 100), skip (default 0)
        //   - userId (Guid) — filter by target user
        //   - changedByUserId (Guid) — filter by actor who performed the change
        //   - from (DateTime, UTC) — inclusive lower bound on ChangedAt
        //   - to (DateTime, UTC) — inclusive upper bound on ChangedAt
        // Output: 200 OK with array of { id, userId, changedByUserId, changedByEmail, oldRoles, newRoles, changedAt } sorted by ChangedAt DESC.
        // Headers: X-Total-Count with total rows for the tenant.
        // Errors: 400 invalid tenant claim; 403 tenant mismatch; 401 handled by auth middleware.
        api.MapGet("/tenants/{tenantId:guid}/audits", async (
            HttpRequest req,
            Guid tenantId,
            int? take,
            int? skip,
            Guid? userId,
            Guid? changedByUserId,
            DateTime? from,
            DateTime? to,
            ClaimsPrincipal user,
            AppDbContext db,
            HttpResponse resp) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            // Additional GUID format validation (defense-in-depth for bad query strings before EF translates)
            var qs = req.Query;
            if (qs.TryGetValue("userId", out var uVals) && uVals.Count > 0 && !Guid.TryParse(uVals[0], out _))
                return Results.BadRequest(new { error = "invalid userId" });
            if (qs.TryGetValue("changedByUserId", out var cVals) && cVals.Count > 0 && !Guid.TryParse(cVals[0], out _))
                return Results.BadRequest(new { error = "invalid changedByUserId" });

            int takeVal = Math.Clamp(take.GetValueOrDefault(50), 1, 100);
            int skipVal = Math.Max(0, skip.GetValueOrDefault(0));

            // Build query with optional filters
            var q = db.Audits.AsNoTracking().Where(a => a.TenantId == tenantId);
            if (userId.HasValue) q = q.Where(a => a.UserId == userId);
            if (changedByUserId.HasValue) q = q.Where(a => a.ChangedByUserId == changedByUserId);
            if (from.HasValue && to.HasValue && from > to)
                return Results.BadRequest(new { error = "invalid range: from must be <= to" });
            if (from.HasValue) q = q.Where(a => a.ChangedAt >= from.Value);
            if (to.HasValue) q = q.Where(a => a.ChangedAt <= to.Value);
            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(a => a.ChangedAt)
                .Skip(skipVal)
                .Take(takeVal)
                .Select(a => new
                {
                    id = a.Id,
                    userId = a.UserId,
                    changedByUserId = a.ChangedByUserId,
                    changedByEmail = a.ChangedByEmail,
                    oldRoles = a.OldRoles,
                    newRoles = a.NewRoles,
                    changedAt = a.ChangedAt
                })
                .ToListAsync();

            resp.Headers["X-Total-Count"] = total.ToString();
            return Results.Ok(items);
        }).RequireAuthorization("TenantAdmin");
        // Purpose: Replace the Roles flags bitfield for a member using an array of enum names; enables Admins to manage granular permissions.
        // Auth: Requires TenantAdmin for the current tenant. Also validates route tenantId matches caller's tenant_id claim.
        // Inputs: route tenantId (Guid), userId (Guid); body { roles: string[] } — case-insensitive enum names like "TenantAdmin", "Creator".
        // Behavior: Parses names → bitfield; if unchanged, returns 204 (no-op). On change, updates membership (immutable replace for InMemory provider parity).
        // Invariant: Must not remove the last TenantAdmin for the tenant considering both legacy Role (Owner/Admin) and flags; violation → 409 Conflict.
        // Responses: 200 with summary { userId, roles, rolesValue } on change; 204 no content on no-op; 400 invalid input; 404 membership not found; 403 tenant mismatch; 401 handled by auth.
        api.MapPost("/tenants/{tenantId:guid}/memberships/{userId:guid}/roles", async (
            Guid tenantId,
            Guid userId,
            ClaimsPrincipal user,
            AppDbContext db,
            SetRolesDto dto) =>
        {
            if (dto is null || dto.Roles is null)
                return Results.BadRequest(new { error = "roles are required" });

            // Validate tenant claim vs route for defense-in-depth.
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var target = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
            if (target is null) return Results.NotFound();

            // Parse flags from provided names; ignore case; invalid names -> 400.
            // Note: Using Enum.TryParse keeps accepted values aligned with the Roles enum; callers may mix case.
            Roles newFlags = Roles.None;
            foreach (var name in dto.Roles)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!Enum.TryParse<Roles>(name, ignoreCase: true, out var flag))
                    return Results.BadRequest(new { error = $"invalid role flag: {name}" });
                newFlags |= flag;
            }

            // If no change, return 204 to indicate idempotent update without altering state.
            if (target.Roles == newFlags)
                return Results.NoContent();

            // Invariant: ensure at least one TenantAdmin remains when removing TenantAdmin from this target.
            // Admin status is determined by either legacy Role (Owner/Admin) OR flags including Roles.TenantAdmin.
            bool targetWasTenantAdmin = (target.Roles & Roles.TenantAdmin) != 0;
            bool targetWillBeTenantAdmin = (newFlags & Roles.TenantAdmin) != 0;
            if (targetWasTenantAdmin && !targetWillBeTenantAdmin)
            {
                // Count other admins (active) across the tenant using the same hybrid definition.
                var otherAdminsCount = await db.Memberships.AsNoTracking()
                    .Where(m => m.TenantId == tenantId && m.UserId != target.UserId && m.Status == MembershipStatus.Active)
                    .CountAsync(m => (m.Roles & Roles.TenantAdmin) != 0);
                if (otherAdminsCount <= 0)
                {
                    return Results.Conflict(new { error = "cannot remove the last TenantAdmin" });
                }
            }

            // Persistence strategy & provider parity:
            // We always replace the membership row (remove + add) to keep EF InMemory behavior aligned with relational.
            // Previous implementation had duplicated branches (no-tx / current-tx / begin new tx) which under InMemory
            // could cascade into executing raw SQL (set_config) unsupported by the provider, yielding a 500.
            // Simplify to a single path; if the provider supports explicit transactions, wrap in a tenant-scoped tx.
            async Task ReplaceAsync()
            {
                var replacement = new Membership
                {
                    Id = target.Id,
                    TenantId = target.TenantId,
                    UserId = target.UserId,
                    Roles = newFlags,
                    Status = target.Status,
                    CreatedAt = target.CreatedAt
                };
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                db.Memberships.Add(replacement);
                await db.SaveChangesAsync();

                Guid? changedByUserId = null;
                var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(sub, out var subGuid)) changedByUserId = subGuid;
                // Robust email resolution: support both JWT registered 'email' and ClaimTypes.Email mappings.
                var changedByEmail = user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email);
                db.Audits.Add(new Audit
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = replacement.UserId,
                    ChangedByUserId = changedByUserId,
                    ChangedByEmail = changedByEmail,
                    OldRoles = target.Roles,
                    NewRoles = newFlags,
                    ChangedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            if (db.Database.SupportsExplicitTransactions())
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                // set_config only valid for relational provider; wrap in try to avoid InMemory issues if capability check regresses
                try { await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString()); } catch { /* no-op for non-relational */ }
                await ReplaceAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ReplaceAsync();
            }

            // Return updated roles summary (string and numeric) for caller UI reconciliation.
            return Results.Ok(new { userId = target.UserId, roles = newFlags.ToString(), rolesValue = (int)newFlags });
        }).RequireAuthorization("TenantAdmin");

        // POST /api/invites/accept — Accept an invite as the signed-in user
        api.MapPost("/invites/accept", async (
            ClaimsPrincipal user,
            AppDbContext db,
            AcceptInviteDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
                return Results.BadRequest(new { error = "token is required" });

            // Must be authenticated
            var userIdStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email);
            if (!Guid.TryParse(userIdStr, out var userId) || string.IsNullOrWhiteSpace(email))
                return Results.Unauthorized();

            // Lookup invitation by token (not tenant-scoped)
            var invite = await db.Invitations.FirstOrDefaultAsync(i => i.Token == dto.Token);
            if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
                return Results.BadRequest(new { error = "invalid or expired invite" });

            // Email must match invitee (case-insensitive)
            if (!string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();

            // Create membership in the inviting tenant (idempotent)
            var tenantId = invite.TenantId;
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant is null) return Results.BadRequest(new { error = "invalid tenant on invite" });

            var existingMembership = await db.Memberships.AsNoTracking()
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);

            var created = false;

            if (!db.Database.SupportsExplicitTransactions())
            {
                if (existingMembership is null)
                {
                    db.Memberships.Add(new Membership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = userId,
                        Roles = invite.Roles,
                        Status = MembershipStatus.Active,
                        CreatedAt = DateTime.UtcNow
                    });
                    created = true;
                }
                // Mark invite accepted (idempotent)
                invite.AcceptedAt = invite.AcceptedAt ?? DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            else
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
                if (existingMembership is null)
                {
                    db.Memberships.Add(new Membership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = userId,
                        Roles = invite.Roles,
                        Status = MembershipStatus.Active,
                        CreatedAt = DateTime.UtcNow
                    });
                    created = true;
                }
                invite.AcceptedAt = invite.AcceptedAt ?? DateTime.UtcNow;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            return Results.Ok(new
            {
                tenantId = tenant.Id,
                tenantSlug = tenant.Name,
                roles = invite.Roles.ToString(),
                rolesValue = (int)invite.Roles,
                membershipCreated = created,
                acceptedAt = invite.AcceptedAt
            });
        })
        // Require authentication (any authenticated user) so JwtBearer runs and populates ClaimsPrincipal.
        // Without this, the endpoint executed with an unauthenticated (empty) principal, causing the
        // internal GUID/email extraction to fail and return 401 even when a valid Bearer token was supplied.
        .RequireAuthorization();

        // Dev utility endpoint always mapped (internal production guard) to simplify tests & ensure discoverability.
        apiRoot.MapPost("/dev/grant-roles", async (HttpContext http, AppDbContext db, GrantRolesRequest req) =>
        {
            // Internal guard: if a key is configured, require matching x-dev-grant-key header
            if (!string.IsNullOrEmpty(devGrantKey))
            {
                if (!http.Request.Headers.TryGetValue("x-dev-grant-key", out var provided) || provided.Count == 0 || !string.Equals(provided[0], devGrantKey, StringComparison.Ordinal))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }
            if (req is null || string.IsNullOrWhiteSpace(req.Email) || (req.TenantId == Guid.Empty && string.IsNullOrWhiteSpace(req.TenantSlug)) || req.Roles is null || req.Roles.Length == 0)
                return Results.BadRequest(new { error = "email, tenantId(or tenantSlug) and roles[] are required" });

            if (!TryParseRoleNames(req.Roles, out var flags, out var invalid))
                return Results.BadRequest(new { error = $"invalid role: {invalid}" });

            var email = req.Email.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            // Ensure tenant exists
            Tenant? tenant = null;
            if (req.TenantId != Guid.Empty)
            {
                tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TenantId);
            }
            else if (!string.IsNullOrWhiteSpace(req.TenantSlug))
            {
                tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == req.TenantSlug);
            }
            if (tenant is null) return Results.BadRequest(new { error = "tenant not found" });

            // Fetch existing membership (tracked) if present.
            var membership = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenant.Id && m.UserId == user.Id);
            if (membership is null)
            {
                membership = new Membership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    Roles = flags,
                    Status = MembershipStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };
                db.Memberships.Add(membership);
            }
            else
            {
                // Apply change & produce audit if roles differ
                var audit = membership.ApplyRoleChange(flags, changedByEmail: "dev-grant-roles", changedByUserId: null);
                if (audit is not null)
                {
                    db.Add(audit);
                }
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { userId = user.Id, tenantId = tenant.Id, roles = flags.ToString(), rolesValue = (int)flags });
        });

        // Diagnostics removed: endpoint enumeration used during earlier routing investigation has been cleaned up.

        // UPROF-11: Denomination presets metadata endpoint
    apiRoot.MapGet("/metadata/denominations", async (HttpContext ctx) =>
        {
            // Auth required; dev header shortcut removed (RDH). Require standard authentication.
            if (ctx.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            // Load static JSON file; future enhancement: move to DB + versioning
            var file = Path.Combine(AppContext.BaseDirectory, "App", "Data", "denominations.json");
            Stream? dataStream = null;
            if (System.IO.File.Exists(file))
            {
                dataStream = System.IO.File.OpenRead(file);
            }
            else
            {
                // Fallback to embedded resource (ensures availability in test contexts / single-file publish)
                var asm = typeof(Program).Assembly;
                var resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("denominations.json", StringComparison.OrdinalIgnoreCase));
                if (resourceName != null)
                {
                    dataStream = asm.GetManifestResourceStream(resourceName);
                }
            }
            if (dataStream == null)
            {
                return Results.Problem(title: "Denominations data missing", statusCode: 500, detail: "denominations.json not found");
            }
            await using (dataStream)
            {
                // Read raw JSON text; cheaper & avoids lifetime concerns of JsonDocument / JsonNode (UPROF-11 fix)
                using var reader = new StreamReader(dataStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                var json = await reader.ReadToEndAsync();
                // Wrap inside an envelope object: { "presets": <original-array-or-object> }
                // We avoid reparsing by constructing a small JSON string. Assumes source file is trusted and valid JSON.
                var wrapped = string.Concat("{\"presets\":", json, "}");
                // Return as application/json without reserialization (prevents double-encoding)
                return Results.Text(wrapped, "application/json", Encoding.UTF8);
            }
    }).WithName("GetDenominationPresets").WithSummary("List denomination presets").WithDescription("Returns an array of denomination preset definitions (id, name, notes)").AllowAnonymous();
        // ---------------------------------------------------------------------
        // TEST HELPERS (Story 2a)
        // ---------------------------------------------------------------------
        var config = app.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var testHelpersEnabled = !env.IsProduction() && (config["AUTH__TEST_HELPERS_ENABLED"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        if (testHelpersEnabled)
        {
            apiRoot.MapPost("/test/mint-tenant-token", async (AppDbContext db, IJwtTokenService jwt, IRefreshTokenService refreshSvc, MintTenantTokenRequest dto) =>
            {
                if (dto is null || string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest(new { error = "email is required" });
                var email = dto.Email.Trim();
                var now = DateTime.UtcNow;
                var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user is null)
                {
                    user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = now };
                    db.Users.Add(user);
                    await db.SaveChangesAsync();
                }
                var memberships = await db.Memberships.AsNoTracking().Where(m => m.UserId == user.Id).ToListAsync();
                if (memberships.Count == 0)
                {
                    // Create personal tenant + membership
                    var local = email.Split('@')[0].ToLowerInvariant();
                    var baseSlug = $"{local}-personal"; var slug = baseSlug; var attempt = 1;
                    while (await db.Tenants.AsNoTracking().AnyAsync(x => x.Name == slug)) { attempt++; slug = $"{baseSlug}-{attempt}"; }
                    var tenant = new Tenant { Id = Guid.NewGuid(), Name = slug, CreatedAt = now };
                    db.Tenants.Add(tenant); await db.SaveChangesAsync();
                    var membership = new Membership { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = user.Id, Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner, Status = MembershipStatus.Active, CreatedAt = now };
                    db.Memberships.Add(membership); await db.SaveChangesAsync();
                    memberships = new List<Membership> { membership };
                }
                var tenantIds = memberships.Select(m => m.TenantId).Distinct().ToList();
                var tenantLookup = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
                var proj = memberships.Select(m => new { tenantId = m.TenantId, tenantSlug = tenantLookup.TryGetValue(m.TenantId, out var n) ? n : string.Empty, roles = (int)m.Roles }).ToList();
                var neutralAccess = jwt.IssueNeutralToken(user.Id.ToString(), user.TokenVersion, user.Email);
                var refreshTtlDays = int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__REFRESH_TTL_DAYS"), out var d) ? d : 30;
                var (refreshId, refreshToken, refreshExpires) = await refreshSvc.IssueNeutralAsync(user.Id, refreshTtlDays);
                var accessExpires = DateTime.UtcNow.AddMinutes(int.TryParse(Environment.GetEnvironmentVariable("AUTH__JWT__ACCESS_TTL_MINUTES"), out var m) ? m : 15);
                object? tenantToken = null; Guid? selectedTenant = null; int rolesBitmask = 0; string tenantSlugSel = string.Empty;
                if (proj.Count == 1 && (dto.AutoTenant == true || string.IsNullOrWhiteSpace(dto.Tenant)))
                {
                    selectedTenant = proj[0].tenantId; tenantSlugSel = proj[0].tenantSlug; rolesBitmask = proj[0].roles;
                }
                else if (!string.IsNullOrWhiteSpace(dto.Tenant))
                {
                    var match = proj.FirstOrDefault(p => string.Equals(p.tenantSlug, dto.Tenant, StringComparison.OrdinalIgnoreCase) || p.tenantId.ToString() == dto.Tenant);
                    if (match != null) { selectedTenant = match.tenantId; tenantSlugSel = match.tenantSlug; rolesBitmask = match.roles; }
                }
                else if (dto.AutoTenant == true && proj.Count > 1)
                {
                    return Results.StatusCode(StatusCodes.Status409Conflict);
                }
                // RDH Story 2 (Option B): Force all role flags for selected tenant when requested
                if (selectedTenant.HasValue && dto.ForceAllRoles == true)
                {
                    // Ensure membership exists (it should) then upgrade flags if not already full set
                    var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == selectedTenant.Value);
                    if (membership != null)
                    {
                        var full = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner;
                        if ((membership.Roles & full) != full)
                        {
                            membership.Roles |= full;
                            await db.SaveChangesAsync();
                        }
                        rolesBitmask = (int)full;
                    }
                }
                // Extra test-only claims (superadmin) support
                var extraClaims = new List<System.Security.Claims.Claim>();
                if (dto.SuperAdmin == true)
                {
                    extraClaims.Add(new System.Security.Claims.Claim("superadmin", "true"));
                }
                if (selectedTenant.HasValue)
                {
                    var tenantAccess = jwt.IssueTenantToken(user.Id.ToString(), selectedTenant.Value, tenantSlugSel, rolesBitmask, user.TokenVersion, user.Email, extraClaims);
                    tenantToken = new { access = new { token = tenantAccess, expiresAt = accessExpires, type = "tenant", tenantId = selectedTenant.Value, tenantSlug = tenantSlugSel } };
                }
                // Story 8: conditional plaintext refresh token
                var exposePlainFlag = configuration.GetValue<bool>("AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", true);
                object refreshObj;
                if (exposePlainFlag)
                {
                    refreshObj = new { token = refreshToken, expiresAt = refreshExpires, type = "neutral" };
                }
                else
                {
                    refreshObj = new { expiresAt = refreshExpires, type = "neutral" };
                }
                // If only neutral token requested but superadmin flag set, re-issue neutral with claim (kept separate for clarity)
                if (dto.SuperAdmin == true && !selectedTenant.HasValue)
                {
                    neutralAccess = jwt.IssueNeutralToken(user.Id.ToString(), user.TokenVersion, user.Email, extraClaims);
                }
                return Results.Ok(new { user = new { user.Id, user.Email }, memberships = proj, access = new { token = neutralAccess, expiresAt = accessExpires, type = "neutral" }, refresh = refreshObj, tenantToken });
            }).WithTags("TestHelpers").WithDescription("Non-production test-only token mint helper").AllowAnonymous();
        }

        return app;
    }

    /// <summary>
    /// Request payload to create a new lesson.
    /// </summary>
    /// <param name="Title">Optional title for the new lesson. Defaults to "Untitled" when omitted.</param>
    public record NewLessonDto(string? Title);
    /// <summary>
    /// Signup request to create a new user. Optionally accepts an invite token to join an existing tenant.
    /// </summary>
    /// <param name="Email">User's email address.</param>
    /// <param name="Password">User's chosen password.</param>
    /// <param name="InviteToken">Optional invite token to join a tenant during signup.</param>
    public record SignupDto(string Email, string Password, string? InviteToken = null);
    public record LoginDto(string Email, string Password);
    /// <summary>
    /// Invite creation payload. Legacy <c>Role</c> is deprecated and rejected (LEGACY_ROLE_DEPRECATED). Supply either
    /// <c>Roles</c> (array of flag names) or <c>RolesValue</c> (bitmask). At least one flag required.
    /// </summary>
    /// <param name="Email">Invite target email.</param>
    /// <param name="Roles">Array of role flag names (TenantAdmin, Approver, Creator, Learner).</param>
    /// <param name="RolesValue">Integer bitmask of roles (OR of flag values).</param>
    public record InviteRequest(string Email, string[]? Roles = null, int? RolesValue = null);
    public record AcceptInviteDto(string Token);
    /// <summary>
    /// Magic-link request payload.
    /// </summary>
    /// <param name="Email">Destination email to receive the sign-in link.</param>
    public record MagicRequestDto(string Email);
    public record MagicConsumeDto(string Token);
    public record SetRolesDto(string[] Roles);
    public record UpdateMemberStatusDto(bool Active);
    public record ResetPasswordDto(string Token, string NewPassword);
    public record ChangePasswordDto(string CurrentPassword, string NewPassword);
    // RDH Story 2 Option B: ForceAllRoles grants full flag set for selected tenant when true (test-only)
    internal sealed record MintTenantTokenRequest(string Email, string? Tenant, bool? AutoTenant, bool? ForceAllRoles, bool? SuperAdmin);
    // Story 3 DTO: tenant selection using neutral refresh token
    internal record SelectTenantDto(string Tenant, string RefreshToken);

    // Helper: parse roles flag names into bitfield; invalid names result in BadRequest at call sites
    private static bool TryParseRoleNames(string[]? names, out Roles flags, out string? invalidName)
    {
        flags = Roles.None;
        invalidName = null;
        if (names is null || names.Length == 0) return true;
        foreach (var n in names)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (!Enum.TryParse<Roles>(n, ignoreCase: true, out var f))
            {
                invalidName = n;
                return false;
            }
            flags |= f;
        }
        return true;
    }

    // Legacy role mapping helper removed (Story 4 Phase 2); callers now provide explicit flags.

    private static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Central helper (Story 5a follow-up) to issue the refresh cookie with consistent attributes.
    /// Secure flag is set when the incoming request is HTTPS OR X-Forwarded-Proto == https (reverse proxy / test simulation).
    /// SameSite=Lax, HttpOnly, Path root. Consolidates previously duplicated blocks in login, magic consume, and select-tenant endpoints.
    /// </summary>
    private static void IssueRefreshCookie(HttpContext http, string refreshToken, DateTime expiresUtc)
    {
        var isHttps = http.Request.IsHttps; // Keep local for clarity / future extension
        // Secure flag depends solely on Request.IsHttps (tests can simulate HTTPS by setting it via test middleware)
        http.Response.Cookies.Append("rt", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiresUtc,
            Path = "/"
        });
    }

    // Story 9: Simple in-memory rate limiter (best effort; process-local). Window 60s, limit 20.
    private static class RefreshRateLimiter
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, List<DateTime>> _events = new();
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
        private const int Limit = 20;

        public static bool ShouldLimit(string key)
        {
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                if (!_events.TryGetValue(key, out var list))
                {
                    list = new List<DateTime>(Limit + 4);
                    _events[key] = list;
                }
                list.RemoveAll(t => (now - t) > Window);
                if (list.Count >= Limit)
                {
                    return true;
                }
                list.Add(now);
                return false;
            }
        }
    }
}

// Dev DTO: request body for /api/dev/grant-roles (either TenantId or TenantSlug required)
public record GrantRolesRequest(Guid TenantId, string? TenantSlug, string Email, string[] Roles);
