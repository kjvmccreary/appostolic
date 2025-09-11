using Microsoft.EntityFrameworkCore;
using Npgsql;
using AppDb = AppDbContext;

// Seed script: idempotent inserts for user/tenants/memberships and one lesson.

var cs = SeedHelpers.BuildConnectionStringFromPgEnv();

var options = new DbContextOptionsBuilder<AppDb>()
    .UseNpgsql(cs, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "app"))
    .Options;

using var db = new AppDb(options);

await db.Database.OpenConnectionAsync();
Console.WriteLine("Connected to database.");

// Ensure schema exists
await db.Database.MigrateAsync();

try
{
    var userEmail = "kevin@example.com";
    var personalSlug = "kevin-personal";
    var orgSlug = "first-baptist-austin";
    const string welcomeTitle = "Welcome draft";

    User user;
    Tenant personal;
    Tenant org;
    Membership mPersonal;
    Membership mOrg;
    Lesson? personalLesson = null;

    // User by natural key (email)
    user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == userEmail)
           ?? new User { Id = Guid.NewGuid(), Email = userEmail, CreatedAt = DateTime.UtcNow };
    if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == user.Id))
    {
        db.Users.Add(user);
        await db.SaveChangesAsync();
        Console.WriteLine($"Created user: {user.Id} {user.Email}");
    }
    else
    {
        Console.WriteLine($"User exists: {user.Id} {user.Email}");
    }

    // Tenants by natural key (slug/name)
    personal = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == personalSlug)
               ?? new Tenant { Id = Guid.NewGuid(), Name = personalSlug, CreatedAt = DateTime.UtcNow };
    if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == personal.Id))
    {
        db.Tenants.Add(personal);
        await db.SaveChangesAsync();
        Console.WriteLine($"Created tenant(personal/free): {personal.Id} {personal.Name}");
    }
    else
    {
        Console.WriteLine($"Tenant exists: {personal.Id} {personal.Name}");
    }

    org = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == orgSlug)
          ?? new Tenant { Id = Guid.NewGuid(), Name = orgSlug, CreatedAt = DateTime.UtcNow };
    if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == org.Id))
    {
        db.Tenants.Add(org);
        await db.SaveChangesAsync();
        Console.WriteLine($"Created tenant(org/plan=org): {org.Id} {org.Name}");
    }
    else
    {
        Console.WriteLine($"Tenant exists: {org.Id} {org.Name}");
    }

    // =====================
    // Personal tenant scope
    // =====================
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
        // Set tenant context inside transaction
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", personal.Id.ToString());
        Console.WriteLine($"tenant context set for {personal.Id}");

        // Membership (idempotent) under RLS
        mPersonal = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == personal.Id && m.UserId == user.Id)
                    ?? new Membership { Id = Guid.NewGuid(), TenantId = personal.Id, UserId = user.Id, Role = MembershipRole.Owner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow };
        if (!await db.Memberships.AsNoTracking().AnyAsync(m => m.Id == mPersonal.Id))
        {
            db.Memberships.Add(mPersonal);
            await db.SaveChangesAsync();
            Console.WriteLine($"Created membership(personal): {mPersonal.Id}");
        }
        else
        {
            Console.WriteLine($"Membership exists(personal): {mPersonal.Id}");
        }

        // Lesson (singleton) under RLS
        personalLesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == personal.Id && l.Title == welcomeTitle);
        if (personalLesson is null)
        {
            personalLesson = new Lesson
            {
                Id = Guid.NewGuid(),
                TenantId = personal.Id,
                Title = welcomeTitle,
                Status = LessonStatus.Draft,
                Audience = LessonAudience.All,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Lessons.Add(personalLesson);
            await db.SaveChangesAsync();
            Console.WriteLine($"Created lesson(personal): {personalLesson.Id}");
        }
        else
        {
            Console.WriteLine($"Lesson exists(personal): {personalLesson.Id}");
        }

        await tx.CommitAsync();
    }

    // =================
    // Org tenant scope
    // =================
    await using (var tx = await db.Database.BeginTransactionAsync())
    {
        // Set tenant context inside transaction
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", org.Id.ToString());
        Console.WriteLine($"tenant context set for {org.Id}");

        // Membership (idempotent) under RLS
        mOrg = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == org.Id && m.UserId == user.Id)
               ?? new Membership { Id = Guid.NewGuid(), TenantId = org.Id, UserId = user.Id, Role = MembershipRole.Owner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow };
        if (!await db.Memberships.AsNoTracking().AnyAsync(m => m.Id == mOrg.Id))
        {
            db.Memberships.Add(mOrg);
            await db.SaveChangesAsync();
            Console.WriteLine($"Created membership(org): {mOrg.Id}");
        }
        else
        {
            Console.WriteLine($"Membership exists(org): {mOrg.Id}");
        }

        await tx.CommitAsync();
    }

    // One-line summary table
    Console.WriteLine();
    Console.WriteLine("| User ID | Personal Tenant (ID/slug) | Org Tenant (ID/slug) | Lesson ID |");
    Console.WriteLine("|---------|----------------------------|-----------------------|-----------|");
    Console.WriteLine($"| {user.Id} | {personal.Id}/{personal.Name} | {org.Id}/{org.Name} | {personalLesson?.Id} |");
    Console.WriteLine();

    Environment.Exit(0);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Seed failed: {ex.Message}");
    throw;
}

static class SeedHelpers
{
    public static Task SetTenantAsync(AppDb db, Guid tenantId)
        => db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());

    public static string BuildConnectionStringFromPgEnv()
    {
        // Optionally read .env for PG* defaults
        var dotenv = LoadDotEnv();

        string host = EnvOr("PGHOST", "localhost");
        string port = EnvOr("PGPORT", "55432");
        string db = EnvOr("PGDATABASE", "appdb");
        string user = EnvOr("PGUSER", dotenv.TryGetValue("PGUSER", out var u) ? u : "");
        string pass = EnvOr("PGPASSWORD", dotenv.TryGetValue("PGPASSWORD", out var p) ? p : "");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var prt) ? prt : 55432,
            Database = db,
            Username = user,
            Password = pass,
            SslMode = SslMode.Disable
        };
        return builder.ConnectionString;
    }

    private static string EnvOr(string key, string fallback) => Environment.GetEnvironmentVariable(key) ?? fallback;

    private static Dictionary<string, string> LoadDotEnv()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { ".env", "../.env", "../../.env", "../../../.env", "../../../../.env" })
        {
            try
            {
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;
                    var idx = trimmed.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = trimmed.Substring(0, idx).Trim();
                    var val = trimmed.Substring(idx + 1).Trim();
                    dict[key] = val;
                }
                // stop at first found
                break;
            }
            catch { /* ignore */ }
        }
        return dict;
    }
}
