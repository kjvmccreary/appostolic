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

        apiRoot.MapPost("/auth/magic/consume", async (AppDbContext db, MagicConsumeDto dto) =>
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
                var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
                if (isInMemory)
                {
                    var membership = new Membership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        UserId = user.Id,
                        Role = MembershipRole.Owner,
                        Roles = DeriveFlagsFromLegacy(MembershipRole.Owner),
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
                        Role = MembershipRole.Owner,
                        Roles = DeriveFlagsFromLegacy(MembershipRole.Owner),
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

            // Return minimal payload including memberships
            var memberships = await db.Memberships.AsNoTracking()
                .Where(m => m.UserId == user!.Id)
                .Join(db.Tenants.AsNoTracking(), m => m.TenantId, tn => tn.Id, (m, tn) => new
                {
                    tenantId = tn.Id,
                    tenantSlug = tn.Name,
                    role = m.Role.ToString()
                })
                .ToListAsync();

            return Results.Ok(new { user = new { user!.Id, user.Email }, memberships });
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
            MembershipRole role;
            Roles flags;
            if (!string.IsNullOrWhiteSpace(dto.InviteToken))
            {
                // Load tracked invitation to mark as accepted
                var invite = await db.Invitations.FirstOrDefaultAsync(i => i.Token == dto.InviteToken);
                if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
                    return Results.BadRequest(new { error = "invalid or expired invite" });
                tenantId = invite.TenantId;
                role = invite.Role;
                flags = invite.Roles == Roles.None ? DeriveFlagsFromLegacy(role) : invite.Roles;
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
                role = MembershipRole.Owner;
                flags = DeriveFlagsFromLegacy(role);
            }

            // Create membership under RLS by setting tenant context within a transaction
            var membership = new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Role = role,
                Roles = flags,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            // In tests we swap to EF InMemory which does not support transactions.
            // Detect provider and skip explicit transaction + set_config when InMemory.
            var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            if (isInMemory)
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
        // Verifies user credentials using Argon2id and returns minimal user payload on success.
        apiRoot.MapPost("/auth/login", async (AppDbContext db, Appostolic.Api.Application.Auth.IPasswordHasher hasher, LoginDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                return Results.BadRequest(new { error = "email and password are required" });

            var email = dto.Email.Trim();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || user.PasswordHash is null || user.PasswordSalt is null)
                return Results.Unauthorized();

            // We currently don't persist iterations; pass 0 to use hasher's default.
            var ok = hasher.Verify(dto.Password, user.PasswordHash!, user.PasswordSalt!, 0);
            if (!ok) return Results.Unauthorized();

            // Include memberships (tenant + role) to support two-stage tenant selection in web.
            var memberships = await db.Memberships.AsNoTracking()
                .Where(m => m.UserId == user.Id)
                .Join(db.Tenants.AsNoTracking(), m => m.TenantId, t => t.Id, (m, t) => new
                {
                    tenantId = t.Id,
                    tenantSlug = t.Name,
                    role = m.Role.ToString()
                })
                .ToListAsync();

            return Results.Ok(new { user.Id, user.Email, memberships });
        }).AllowAnonymous();

        var api = apiRoot.RequireAuthorization();
        // Change password (authenticated)
        api.MapPost("/auth/change-password", async (ClaimsPrincipal principal, AppDbContext db, Appostolic.Api.Application.Auth.IPasswordHasher hasher, ChangePasswordDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return Results.BadRequest(new { error = "currentPassword and newPassword are required" });
            var email = principal.FindFirstValue("email");
            if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || user.PasswordHash is null || user.PasswordSalt is null)
                return Results.Unauthorized();
            var ok = hasher.Verify(dto.CurrentPassword, user.PasswordHash!, user.PasswordSalt!, 0);
            if (!ok) return Results.Unauthorized();
            var (hash, salt, iterations) = hasher.HashPassword(dto.NewPassword);
            var updated = user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
            db.Users.Update(updated);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // GET /api/me
        api.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var sub = user.FindFirstValue("sub") ?? string.Empty;
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
            var userIdStr = user.FindFirstValue("sub");
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

            var userIdStr = user.FindFirstValue("sub");
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if (me.Role != MembershipRole.Owner && me.Role != MembershipRole.Admin) return Results.Forbid();

            var members = await db.Memberships.AsNoTracking()
                .Where(m => m.TenantId == tenantId)
                .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new
                {
                    userId = u.Id,
                    email = u.Email,
                    role = m.Role.ToString(),
                    joinedAt = m.CreatedAt
                })
                .OrderBy(x => x.email)
                .ToListAsync();
            return Results.Ok(members);
        }).RequireAuthorization("TenantAdmin");

        // POST /api/tenants/{tenantId}/invites (Admin/Owner only)
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

            var userIdStr = user.FindFirstValue("sub");
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if (me.Role != MembershipRole.Owner && me.Role != MembershipRole.Admin) return Results.Forbid();

            if (dto is null || string.IsNullOrWhiteSpace(dto.Email))
                return Results.BadRequest(new { error = "email is required" });

            var email = dto.Email.Trim();
            var emailLower = email.ToLowerInvariant();
            var role = MembershipRole.Viewer;
            if (!string.IsNullOrWhiteSpace(dto.Role))
            {
                if (!Enum.TryParse<MembershipRole>(dto.Role, true, out role))
                    return Results.BadRequest(new { error = "invalid role" });
            }

            // IAM 2.2: Parse optional roles flags array; if omitted/null/empty, derive from legacy role for compatibility.
            if (!TryParseRoleNames(dto.Roles, out var rolesFlags, out var invalidName))
                return Results.BadRequest(new { error = $"invalid role flag: {invalidName}" });
            if (rolesFlags == Roles.None)
            {
                rolesFlags = DeriveFlagsFromLegacy(role);
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
                    Role = role,
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
                existingInvite.Role = role;
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
                msg.Body = $"<p>Hello,</p><p>You were invited to join <b>{user.FindFirstValue("tenant_slug") ?? tenantId.ToString()}</b> as <b>{role}</b>.</p><p>To proceed, open this link: <a href='{signupUrl}'>Accept invite</a>.</p><p>If you already have an account, you’ll be asked to sign in first. After signing in, your invite will be applied automatically.</p><p>This invite expires at {expiresAt:u}.</p>";
                await client.SendMailAsync(msg);
            }
            catch
            {
                // Best-effort in dev; ignore email failures
            }

            return Results.Created($"/api/tenants/{tenantId}/invites/{email}", new { email, role = role.ToString(), roles = rolesFlags.ToString(), rolesValue = (int)rolesFlags, expiresAt });
        }).RequireAuthorization("TenantAdmin");

        // GET /api/tenants/{tenantId}/invites (Admin/Owner only)
    api.MapGet("/tenants/{tenantId:guid}/invites", async (Guid tenantId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var userIdStr = user.FindFirstValue("sub");
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if (me.Role != MembershipRole.Owner && me.Role != MembershipRole.Admin) return Results.Forbid();

            var invites = await db.Invitations.AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new
                {
                    email = i.Email,
                    role = i.Role.ToString(),
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

            var userIdStr = user.FindFirstValue("sub");
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if (me.Role != MembershipRole.Owner && me.Role != MembershipRole.Admin) return Results.Forbid();

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
                msg.Subject = $"Your invite was re-sent for {user.FindFirstValue("tenant_slug") ?? tenantId.ToString()}";
                var signupUrl = $"http://localhost:3000/invite/accept?token={invite.Token}";
                msg.IsBodyHtml = true;
                msg.Body = $"<p>Hello,</p><p>You were invited to join <b>{user.FindFirstValue("tenant_slug") ?? tenantId.ToString()}</b> as <b>{invite.Role}</b>.</p><p>To proceed, open this link: <a href='{signupUrl}'>Accept invite</a>.</p><p>If you already have an account, you’ll be asked to sign in first. After signing in, your invite will be applied automatically.</p><p>This invite expires at {invite.ExpiresAt:u}.</p>";
                await client.SendMailAsync(msg);
            }
            catch { }

            return Results.Ok(new { email = invite.Email, role = invite.Role.ToString(), roles = invite.Roles.ToString(), rolesValue = (int)invite.Roles, expiresAt = invite.ExpiresAt });
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

            var userIdStr = user.FindFirstValue("sub");
            if (!Guid.TryParse(userIdStr, out var userId)) return Results.BadRequest(new { error = "invalid user" });

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
            if (me is null) return Results.Forbid();
            if (me.Role != MembershipRole.Owner && me.Role != MembershipRole.Admin) return Results.Forbid();

            var lower = email.Trim().ToLowerInvariant();
            var invite = await db.Invitations.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Email.ToLower() == lower);
            if (invite is null) return Results.NotFound();

            db.Invitations.Remove(invite);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("TenantAdmin");

        // PUT /api/tenants/{tenantId}/members/{userId} — change member role (Admin/Owner only)
        api.MapPut("/tenants/{tenantId:guid}/members/{userId:guid}", async (
            Guid tenantId,
            Guid userId,
            ClaimsPrincipal user,
            AppDbContext db,
            UpdateMemberRoleDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Role))
                return Results.BadRequest(new { error = "role is required" });

            var callerIdStr = user.FindFirstValue("sub");
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(callerIdStr, out var callerId)) return Results.Unauthorized();
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == callerId);
            if (me is null) return Results.Forbid();
            var isCallerOwner = me.Role == MembershipRole.Owner;
            var isCallerAdminOrOwner = isCallerOwner || me.Role == MembershipRole.Admin;
            if (!isCallerAdminOrOwner) return Results.Forbid();

            var target = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
            if (target is null) return Results.NotFound();

            if (!Enum.TryParse<MembershipRole>(dto.Role, true, out var newRole))
                return Results.BadRequest(new { error = "invalid role" });

            if (target.Role == newRole)
                return Results.NoContent();

            // Only Owner can assign Owner
            if (newRole == MembershipRole.Owner && !isCallerOwner)
                return Results.Forbid();

            // Owner-specific demotion constraints are superseded by TenantAdmin invariant in Story 1.4

            // Story 1.4: Enforce invariant — at least one TenantAdmin per tenant.
            // If this change would result in zero TenantAdmins (Owner/Admin), block with 409.
            bool isRemovingTenantAdminRole = target.Role is MembershipRole.Owner or MembershipRole.Admin;
            bool becomesTenantAdmin = newRole is MembershipRole.Owner or MembershipRole.Admin;
            if (isRemovingTenantAdminRole && !becomesTenantAdmin)
            {
                var currentAdmins = await db.Memberships.AsNoTracking()
                    .CountAsync(m => m.TenantId == tenantId && (m.Role == MembershipRole.Owner || m.Role == MembershipRole.Admin) && m.Status == MembershipStatus.Active);
                if (currentAdmins <= 1)
                {
                    return Results.Conflict(new { error = "cannot remove the last TenantAdmin" });
                }
            }

            var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            if (isInMemory)
            {
                // Replace immutable record with updated role, preserving CreatedAt
                var replacement = new Membership
                {
                    Id = target.Id,
                    TenantId = target.TenantId,
                    UserId = target.UserId,
                    Role = newRole,
                    Status = target.Status,
                    CreatedAt = target.CreatedAt
                };
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                db.Memberships.Add(replacement);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
            else
            {
                // If middleware already opened a tenant-scoped transaction, reuse it to avoid nesting
                if (db.Database.CurrentTransaction is not null)
                {
                    var replacement = new Membership
                    {
                        Id = target.Id,
                        TenantId = target.TenantId,
                        UserId = target.UserId,
                        Role = newRole,
                        Status = target.Status,
                        CreatedAt = target.CreatedAt
                    };
                    db.Memberships.Remove(target);
                    await db.SaveChangesAsync();
                    db.Memberships.Add(replacement);
                    await db.SaveChangesAsync();
                    return Results.NoContent();
                }
                else
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
                    var replacement = new Membership
                    {
                        Id = target.Id,
                        TenantId = target.TenantId,
                        UserId = target.UserId,
                        Role = newRole,
                        Status = target.Status,
                        CreatedAt = target.CreatedAt
                    };
                    db.Memberships.Remove(target);
                    await db.SaveChangesAsync();
                    db.Memberships.Add(replacement);
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return Results.NoContent();
                }
            }
        }).RequireAuthorization("TenantAdmin");

        // DELETE /api/tenants/{tenantId}/members/{userId} — remove member (Admin/Owner only)
        api.MapDelete("/tenants/{tenantId:guid}/members/{userId:guid}", async (
            Guid tenantId,
            Guid userId,
            ClaimsPrincipal user,
            AppDbContext db) =>
        {
            var callerIdStr = user.FindFirstValue("sub");
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(callerIdStr, out var callerId)) return Results.Unauthorized();
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == callerId);
            if (me is null) return Results.Forbid();
            var isCallerAdminOrOwner = me.Role == MembershipRole.Admin || me.Role == MembershipRole.Owner;
            if (!isCallerAdminOrOwner) return Results.Forbid();

            var target = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
            if (target is null) return Results.NotFound();

            // Story 1.4 invariant: prevent removing the last TenantAdmin (Owner/Admin)
            var adminCount = await db.Memberships.AsNoTracking()
                .CountAsync(m => m.TenantId == tenantId && (m.Role == MembershipRole.Owner || m.Role == MembershipRole.Admin) && m.Status == MembershipStatus.Active);
            if ((target.Role == MembershipRole.Owner || target.Role == MembershipRole.Admin) && adminCount <= 1)
            {
                return Results.Conflict(new { error = "cannot remove the last TenantAdmin" });
            }

            // Forbid removing yourself (only applies when not the last TenantAdmin)
            if (target.UserId == callerId)
                return Results.BadRequest(new { error = "cannot remove yourself" });

            var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            if (isInMemory)
            {
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
            else
            {
                if (db.Database.CurrentTransaction is not null)
                {
                    db.Memberships.Remove(target);
                    await db.SaveChangesAsync();
                    return Results.NoContent();
                }
                else
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
                    db.Memberships.Remove(target);
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return Results.NoContent();
                }
            }
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
                    role = m.Role.ToString(),
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
            var callerIdStr = user.FindFirstValue("sub");
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(callerIdStr, out var callerId)) return Results.Unauthorized();
            if (!Guid.TryParse(tenantIdStr, out var currentTenantId)) return Results.BadRequest(new { error = "invalid tenant" });
            if (tenantId != currentTenantId) return Results.Forbid();

            var me = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == callerId);
            if (me is null) return Results.Forbid();
            var isCallerAdminOrOwner = me.Role == MembershipRole.Admin || me.Role == MembershipRole.Owner;
            if (!isCallerAdminOrOwner) return Results.Forbid();

            var target = await db.Memberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
            if (target is null) return Results.NotFound();

            var newStatus = dto.Active ? MembershipStatus.Active : MembershipStatus.Suspended;
            if (target.Status == newStatus) return Results.NoContent();

            // Enforce invariant: cannot deactivate the last TenantAdmin (hybrid definition).
            if (!dto.Active)
            {
                bool targetIsTenantAdmin = (target.Roles & Roles.TenantAdmin) != 0 || target.Role is MembershipRole.Owner or MembershipRole.Admin;
                if (targetIsTenantAdmin)
                {
                    var otherActiveAdmins = await db.Memberships.AsNoTracking()
                        .Where(m => m.TenantId == tenantId && m.UserId != target.UserId && m.Status == MembershipStatus.Active)
                        .CountAsync(m => ((m.Roles & Roles.TenantAdmin) != 0) || (m.Role == MembershipRole.Owner || m.Role == MembershipRole.Admin));
                    if (otherActiveAdmins <= 0)
                    {
                        return Results.Conflict(new { error = "cannot deactivate the last TenantAdmin" });
                    }
                }
            }

            var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            if (isInMemory)
            {
                var replacement = new Membership
                {
                    Id = target.Id,
                    TenantId = target.TenantId,
                    UserId = target.UserId,
                    Role = target.Role,
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
            else
            {
                if (db.Database.CurrentTransaction is not null)
                {
                    var replacement = new Membership
                    {
                        Id = target.Id,
                        TenantId = target.TenantId,
                        UserId = target.UserId,
                        Role = target.Role,
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
                else
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
                    var replacement = new Membership
                    {
                        Id = target.Id,
                        TenantId = target.TenantId,
                        UserId = target.UserId,
                        Role = target.Role,
                        Roles = target.Roles,
                        Status = newStatus,
                        CreatedAt = target.CreatedAt
                    };
                    db.Memberships.Remove(target);
                    await db.SaveChangesAsync();
                    db.Memberships.Add(replacement);
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    return Results.Ok(new { userId = replacement.UserId, status = replacement.Status.ToString() });
                }
            }
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
            bool targetWasTenantAdmin = (target.Roles & Roles.TenantAdmin) != 0 || target.Role is MembershipRole.Owner or MembershipRole.Admin;
            bool targetWillBeTenantAdmin = (newFlags & Roles.TenantAdmin) != 0 || target.Role is MembershipRole.Owner or MembershipRole.Admin; // legacy role still confers admin
            if (targetWasTenantAdmin && !targetWillBeTenantAdmin)
            {
                // Count other admins (active) across the tenant using the same hybrid definition.
                var otherAdminsCount = await db.Memberships.AsNoTracking()
                    .Where(m => m.TenantId == tenantId && m.UserId != target.UserId && m.Status == MembershipStatus.Active)
                    .CountAsync(m => ((m.Roles & Roles.TenantAdmin) != 0) || (m.Role == MembershipRole.Owner || m.Role == MembershipRole.Admin));
                if (otherAdminsCount <= 0)
                {
                    return Results.Conflict(new { error = "cannot remove the last TenantAdmin" });
                }
            }

            // Persistence strategy note:
            // EF InMemory provider cannot update an immutable record pattern easily, so we replace the row (remove+add)
            // to mirror the relational provider path where we also replace under a tenant-scoped transaction to respect RLS.
            var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            if (isInMemory)
            {
                var replacement = new Membership
                {
                    Id = target.Id,
                    TenantId = target.TenantId,
                    UserId = target.UserId,
                    Role = target.Role,
                    Roles = newFlags,
                    Status = target.Status,
                    CreatedAt = target.CreatedAt
                };
                db.Memberships.Remove(target);
                await db.SaveChangesAsync();
                db.Memberships.Add(replacement);
                await db.SaveChangesAsync();

                // Audit: record roles change
                Guid? changedByUserId = null;
                var changedByStr = user.FindFirstValue("sub");
                if (Guid.TryParse(changedByStr, out var changedByGuid)) changedByUserId = changedByGuid;
                var changedByEmail = user.FindFirstValue("email");
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
            else
            {
                if (db.Database.CurrentTransaction is not null)
                {
                    var replacement = new Membership
                    {
                        Id = target.Id,
                        TenantId = target.TenantId,
                        UserId = target.UserId,
                        Role = target.Role,
                        Roles = newFlags,
                        Status = target.Status,
                        CreatedAt = target.CreatedAt
                    };
                    db.Memberships.Remove(target);
                    await db.SaveChangesAsync();
                    db.Memberships.Add(replacement);
                    await db.SaveChangesAsync();

                    // Audit: record roles change within ambient transaction
                    Guid? changedByUserId = null;
                    var changedByStr = user.FindFirstValue("sub");
                    if (Guid.TryParse(changedByStr, out var changedByGuid)) changedByUserId = changedByGuid;
                    var changedByEmail = user.FindFirstValue("email");
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
                else
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
                    var replacement = new Membership
                    {
                        Id = target.Id,
                        TenantId = target.TenantId,
                        UserId = target.UserId,
                        Role = target.Role,
                        Roles = newFlags,
                        Status = target.Status,
                        CreatedAt = target.CreatedAt
                    };
                    db.Memberships.Remove(target);
                    await db.SaveChangesAsync();
                    db.Memberships.Add(replacement);
                    await db.SaveChangesAsync();

                    // Audit: record roles change within same transaction
                    Guid? changedByUserId = null;
                    var changedByStr = user.FindFirstValue("sub");
                    if (Guid.TryParse(changedByStr, out var changedByGuid)) changedByUserId = changedByGuid;
                    var changedByEmail = user.FindFirstValue("email");
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
                    await tx.CommitAsync();
                }
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
            var userIdStr = user.FindFirstValue("sub");
            var email = user.FindFirstValue("email");
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

            var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            if (isInMemory)
            {
                if (existingMembership is null)
                {
                    db.Memberships.Add(new Membership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = userId,
                        Role = invite.Role,
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
                // Set tenant context for RLS writes to memberships
                await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());

                if (existingMembership is null)
                {
                    db.Memberships.Add(new Membership
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = userId,
                        Role = invite.Role,
                        Roles = invite.Roles,
                        Status = MembershipStatus.Active,
                        CreatedAt = DateTime.UtcNow
                    });
                    created = true;
                }

                // Mark invite accepted (idempotent)
                invite.AcceptedAt = invite.AcceptedAt ?? DateTime.UtcNow;

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            return Results.Ok(new
            {
                tenantId = tenant.Id,
                tenantSlug = tenant.Name,
                role = invite.Role.ToString(),
                roles = invite.Roles.ToString(),
                rolesValue = (int)invite.Roles,
                membershipCreated = created,
                acceptedAt = invite.AcceptedAt
            });
        });

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
                    Role = MembershipRole.Viewer,
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
            if (ctx.User?.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }
            // Load static JSON file; future enhancement: move to DB + versioning
            var file = Path.Combine(AppContext.BaseDirectory, "App", "Data", "denominations.json");
            if (!System.IO.File.Exists(file))
            {
                return Results.Problem(title: "Denominations data missing", statusCode: 500, detail: "denominations.json not found");
            }
            await using var fs = System.IO.File.OpenRead(file);
            using var doc = await JsonDocument.ParseAsync(fs);
            return Results.Json(new { presets = doc.RootElement }, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }).WithName("GetDenominationPresets").WithSummary("List denomination presets").WithDescription("Returns an array of denomination preset definitions (id, name, notes)");

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
    /// Invite request payload to invite a user by email to a tenant.
    /// </summary>
    /// <param name="Email">Invitee email address.</param>
    /// <param name="Role">Legacy coarse role name (Owner/Admin/Editor/Viewer) for compatibility; used to derive flags when Roles are omitted.</param>
    /// <param name="Roles">Optional granular role flags names (e.g., ["TenantAdmin","Creator"]). Case-insensitive; if omitted, flags are derived from Role.</param>
    public record InviteRequest(string Email, string? Role, string[]? Roles = null);
    public record AcceptInviteDto(string Token);
    /// <summary>
    /// Magic-link request payload.
    /// </summary>
    /// <param name="Email">Destination email to receive the sign-in link.</param>
    public record MagicRequestDto(string Email);
    public record MagicConsumeDto(string Token);
    public record UpdateMemberRoleDto(string Role);
    public record SetRolesDto(string[] Roles);
    public record UpdateMemberStatusDto(bool Active);
    public record ResetPasswordDto(string Token, string NewPassword);
    public record ChangePasswordDto(string CurrentPassword, string NewPassword);

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

    // Helper: derive default flags from legacy MembershipRole for compatibility
    private static Roles DeriveFlagsFromLegacy(MembershipRole role)
    {
        return role switch
        {
            MembershipRole.Owner => Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
            MembershipRole.Admin => Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
            MembershipRole.Editor => Roles.Creator | Roles.Learner,
            _ => Roles.Learner,
        };
    }

    private static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// Dev DTO: request body for /api/dev/grant-roles (either TenantId or TenantSlug required)
public record GrantRolesRequest(Guid TenantId, string? TenantSlug, string Email, string[] Roles);
