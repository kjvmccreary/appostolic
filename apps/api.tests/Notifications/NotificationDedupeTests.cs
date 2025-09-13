using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests.Notifications;

public class NotificationDedupeTests
{
    private sealed class OptionsWrapper<T> : IOptions<T> where T : class, new()
    {
        public OptionsWrapper(T value) { Value = value; }
        public T Value { get; }
    }

    private static (ServiceProvider sp, AppDbContext db, INotificationOutbox outbox) Build()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase($"dedupe-{Guid.NewGuid()}"));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var options = new OptionsWrapper<NotificationOptions>(new NotificationOptions { DedupeTtl = TimeSpan.FromMinutes(5) });
        var outbox = new EfNotificationOutbox(db, options);
        return (sp, db, outbox);
    }

    [Fact]
    public async Task CreateQueuedAsync_blocks_duplicate_within_ttl()
    {
        var (_, db, outbox) = Build();

        var msg = new EmailMessage(EmailKind.Verification, "a@x.com", null, new Dictionary<string, object?>(), "k-1");
        var id1 = await outbox.CreateQueuedAsync(msg);
        id1.Should().NotBe(Guid.Empty);

        // Second attempt with same key within TTL should throw
        var act = async () => await outbox.CreateQueuedAsync(msg);
        await act.Should().ThrowAsync<DuplicateNotificationException>();
    }
}
