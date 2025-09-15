using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace Appostolic.Api.App.Endpoints;

public static class V1
{
    public static IEndpointRouteBuilder MapV1Endpoints(this IEndpointRouteBuilder app)
    {
        var apiRoot = app.MapGroup("/api");
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
            if (!string.IsNullOrWhiteSpace(dto.InviteToken))
            {
                // Load tracked invitation to mark as accepted
                var invite = await db.Invitations.FirstOrDefaultAsync(i => i.Token == dto.InviteToken);
                if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
                    return Results.BadRequest(new { error = "invalid or expired invite" });
                tenantId = invite.TenantId;
                role = invite.Role;
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
            }

            // Create membership under RLS by setting tenant context within a transaction
            var membership = new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Role = role,
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
                existingInvite.Token = token;
                existingInvite.ExpiresAt = expiresAt;
                existingInvite.InvitedByUserId = userId;
                existingInvite.AcceptedAt = null;
            }
            await db.SaveChangesAsync();

            // Send dev email via SMTP (Mailhog)
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
                var signupUrl = $"http://localhost:3000/signup?invite={token}";
                msg.Body = $"You've been invited as {role}.\n\nUse this invite token during signup: {token}\n\nOr open: {signupUrl}\n\nThis invite expires at {expiresAt:u}.";
                await client.SendMailAsync(msg);
            }
            catch
            {
                // Best-effort in dev; ignore email failures
            }

            return Results.Created($"/api/tenants/{tenantId}/invites/{email}", new { email, role = role.ToString(), expiresAt });
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
                var signupUrl = $"http://localhost:3000/signup?invite={invite.Token}";
                msg.Body = $"You've been invited as {invite.Role}.\n\nUse this invite token during signup: {invite.Token}\n\nOr open: {signupUrl}\n\nThis invite expires at {invite.ExpiresAt:u}.";
                await client.SendMailAsync(msg);
            }
            catch { }

            return Results.Ok(new { email = invite.Email, role = invite.Role.ToString(), expiresAt = invite.ExpiresAt });
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

            // Cannot demote the last Owner; also prevent self-demotion from last Owner explicitly
            if (target.Role == MembershipRole.Owner && newRole != MembershipRole.Owner)
            {
                var ownerCount = await db.Memberships.AsNoTracking()
                    .CountAsync(m => m.TenantId == tenantId && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Active);
                if (ownerCount <= 1)
                    return Results.BadRequest(new { error = "cannot demote the last Owner" });
                if (target.UserId == callerId)
                    return Results.BadRequest(new { error = "cannot change your own role when you are the last Owner" });
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

            // Forbid removing yourself
            if (target.UserId == callerId)
                return Results.BadRequest(new { error = "cannot remove yourself" });

            // Forbid removing the last Owner
            if (target.Role == MembershipRole.Owner)
            {
                var ownerCount = await db.Memberships.AsNoTracking()
                    .CountAsync(m => m.TenantId == tenantId && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Active);
                if (ownerCount <= 1)
                    return Results.BadRequest(new { error = "cannot remove the last Owner" });
            }

            var isInMemory = db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true;
            if (isInMemory)
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
                membershipCreated = created,
                acceptedAt = invite.AcceptedAt
            });
        });

        return app;
    }

    public record NewLessonDto(string? Title);
    public record SignupDto(string Email, string Password, string? InviteToken = null);
    public record LoginDto(string Email, string Password);
    public record InviteRequest(string Email, string? Role);
    public record AcceptInviteDto(string Token);
    public record MagicRequestDto(string Email);
    public record MagicConsumeDto(string Token);
    public record UpdateMemberRoleDto(string Role);
    public record ResetPasswordDto(string Token, string NewPassword);
    public record ChangePasswordDto(string CurrentPassword, string NewPassword);

    private static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
