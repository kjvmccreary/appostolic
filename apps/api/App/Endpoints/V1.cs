using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace Appostolic.Api.App.Endpoints;

public static class V1
{
    public static IEndpointRouteBuilder MapV1Endpoints(this IEndpointRouteBuilder app)
    {
        var apiRoot = app.MapGroup("/api");
        // Anonymous auth endpoints
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
                // Create personal tenant (slug: {localpart}-personal) if not exists
                var local = email.Split('@')[0].ToLowerInvariant();
                var slug = $"{local}-personal";
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == slug)
                             ?? new Tenant { Id = Guid.NewGuid(), Name = slug, CreatedAt = DateTime.UtcNow };
                if (await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenant.Id) == false)
                {
                    db.Tenants.Add(tenant);
                    await db.SaveChangesAsync();
                }
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
        });

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
        });

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
        });

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
        });

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
        });

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
        });

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
}
