using System.Text;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests.Notifications;

public class FieldEncryptionTests
{
    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Encrypts_And_Decrypts_On_Lease()
    {
        await using var db = NewDb();
        var key = new byte[32];
        new Random().NextBytes(key);
        var opts = Options.Create(new NotificationOptions
        {
            EncryptFields = true,
            EncryptionKeyBase64 = Convert.ToBase64String(key),
            EncryptToName = true,
            EncryptSubject = true,
            EncryptBodyHtml = true,
            EncryptBodyText = true
        });
        var cipher = new AesGcmFieldCipher(key);
        var outbox = new EfNotificationOutbox(db, opts, cipher);

        var msg = new EmailMessage(EmailKind.Verification, "alice@example.com", "Alice", new Dictionary<string, object?> { ["link"] = "/x" }, null);
        var id = await outbox.CreateQueuedAsync(msg, tokenHash: null, snapshots: ("S", "<b>H</b>", "T"));

        // Assert stored values appear encrypted (prefixed) when read raw
        var raw = await db.Notifications.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.StartsWith("enc:v1:", raw.Subject);
        Assert.StartsWith("enc:v1:", raw.BodyHtml);
        Assert.StartsWith("enc:v1:", raw.BodyText);
        Assert.StartsWith("enc:v1:", raw.ToName);

        // Lease should decrypt into entity instance
        var leased = await outbox.LeaseNextDueAsync();
        Assert.NotNull(leased);
        Assert.Equal("S", leased!.Subject);
        Assert.Equal("<b>H</b>", leased.BodyHtml);
        Assert.Equal("T", leased.BodyText);
        Assert.Equal("Alice", leased.ToName);
    }
}
