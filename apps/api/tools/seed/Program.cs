using Microsoft.EntityFrameworkCore;
using Npgsql;

// Seed script: idempotent inserts for user/tenants/memberships and one lesson.

var cs = SeedHelpers.BuildConnectionString();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(cs, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "app"))
    .Options;

using var db = new AppDbContext(options);

await db.Database.OpenConnectionAsync();
Console.WriteLine("Connected to database.");

// Ensure schema exists (in case running against fresh DB without migrations applied)
await db.Database.MigrateAsync();

var email = "kevin@example.com";
var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
if (user is null)
{
    user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    Console.WriteLine($"Created user: {user.Id} {user.Email}");
}
else
{
    Console.WriteLine($"User exists: {user.Id} {user.Email}");
}

// Create tenants
var personalName = "kevin-personal";
var orgName = "first-baptist-austin";

var personal = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == personalName);
if (personal is null)
{
    personal = new Tenant { Id = Guid.NewGuid(), Name = personalName, CreatedAt = DateTime.UtcNow };
    db.Tenants.Add(personal);
    await db.SaveChangesAsync();
    Console.WriteLine($"Created tenant(personal/free): {personal.Id} {personal.Name}");
}
else
{
    Console.WriteLine($"Tenant exists: {personal.Id} {personal.Name}");
}

var org = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == orgName);
if (org is null)
{
    org = new Tenant { Id = Guid.NewGuid(), Name = orgName, CreatedAt = DateTime.UtcNow };
    db.Tenants.Add(org);
    await db.SaveChangesAsync();
    Console.WriteLine($"Created tenant(org/plan=org): {org.Id} {org.Name}");
}
else
{
    Console.WriteLine($"Tenant exists: {org.Id} {org.Name}");
}

// Create memberships as Owner for both tenants
await SeedHelpers.EnsureMembershipAsync(db, personal.Id, user.Id, MembershipRole.Owner, MembershipStatus.Active);
await SeedHelpers.EnsureMembershipAsync(db, org.Id, user.Id, MembershipRole.Owner, MembershipStatus.Active);

// Create one empty lesson for personal tenant
await SeedHelpers.EnsureLessonAsync(db, personal.Id, title: "");

Console.WriteLine("Seed complete.");

static class SeedHelpers
{
    public static async Task EnsureMembershipAsync(AppDbContext db, Guid tenantId, Guid userId, MembershipRole role, MembershipStatus status)
    {
        // Ensure RLS context for membership operations
        await db.Database.ExecuteSqlRawAsync("SELECT app.set_tenant({0})", tenantId);

        var existing = await db.Memberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
        if (existing is not null)
        {
            Console.WriteLine($"Membership exists: tenant={tenantId} user={userId} role={existing.Role} status={existing.Status}");
            return;
        }

        var m = new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        db.Memberships.Add(m);
        await db.SaveChangesAsync();
        Console.WriteLine($"Created membership: {m.Id} tenant={m.TenantId} user={m.UserId} role={m.Role} status={m.Status}");
    }

    public static async Task EnsureLessonAsync(AppDbContext db, Guid tenantId, string title)
    {
        // Set app.tenant_id so RLS allows insert/select
        await db.Database.ExecuteSqlRawAsync("SELECT app.set_tenant({0})", tenantId);

        var exists = await db.Lessons.AsNoTracking().AnyAsync(l => l.TenantId == tenantId && l.Title == title);
        if (exists)
        {
            Console.WriteLine($"Lesson exists for tenant={tenantId} title='{title}'");
            return;
        }

        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title,
            Status = LessonStatus.Draft,
            Audience = LessonAudience.All,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        Console.WriteLine($"Created lesson: {lesson.Id} tenant={lesson.TenantId} title='{lesson.Title}'");
    }

    public static string BuildConnectionString()
    {
        string host = EnvOr("POSTGRES_HOST", "localhost");
        int port = int.Parse(EnvOr("POSTGRES_PORT", "55432"));
        string database = EnvOr("POSTGRES_DB", "app");
        string username = EnvOr("POSTGRES_USER", "app");
        string password = EnvOr("POSTGRES_PASSWORD", "app");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Disable
        };
        return builder.ConnectionString;
    }

    private static string EnvOr(string key, string fallback) => Environment.GetEnvironmentVariable(key) ?? fallback;
}
