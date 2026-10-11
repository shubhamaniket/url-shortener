using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure;
using UrlShortener.Infrastructure.Links;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.Persistence;

/// <summary>The background click writer against a real, migrated SQLite file (one per test).</summary>
public sealed class ClickEventWriterTests : IAsyncLifetime
{
    private const string RawReferer = "https://user:secret@news.example/article?u=42";
    private const string RawUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";

    private static readonly DateTime CreatedAt = new(2026, 10, 10, 13, 45, 0, DateTimeKind.Utc);

    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"urlshortener-writer-{Guid.NewGuid():N}.db");
    private ServiceProvider _services = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task BatchStoresEventsAndIncreasesTotalsTogether()
    {
        await BuildAsync();
        var first = await AddLinkAsync("first");
        var second = await AddLinkAsync("second");
        var writer = await StartWriterAsync();

        Record(Click(first, RawReferer, RawUserAgent), Click(first), Click(first), Click(second), Click(second));

        await Eventually.AssertAsync(async () => await EventCountAsync() == 5, "all five clicks stored");
        Assert.Equal(3, await ClickCountAsync(first));
        Assert.Equal(2, await ClickCountAsync(second));
        await using var scope = _services.CreateAsyncScope();
        var stored = await Db(scope).ClickEvents.AsNoTracking().Where(click => click.ReferrerHost != null).SingleAsync();
        Assert.Equal("news.example", stored.ReferrerHost);
        Assert.Equal(UserAgentFamily.Chrome, stored.UserAgentFamily);
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ClicksForADeletedLinkAreSkippedAndTheRestOfTheBatchIsStored()
    {
        await BuildAsync();
        var live = await AddLinkAsync("live");
        var retired = await AddLinkAsync("retired");
        await using (var scope = _services.CreateAsyncScope())
        {
            await Db(scope).Database.ExecuteSqlRawAsync("DELETE FROM ShortLinks WHERE Id = {0}", retired);
        }

        // Queued before the writer starts, so all four arrive in one batch.
        Record(Click(live), Click(retired), Click(live), Click(retired));
        var writer = await StartWriterAsync();

        await Eventually.AssertAsync(async () => await ClickCountAsync(live) == 2, "the live link's clicks stored");
        Assert.Equal(2, await EventCountAsync());
        Assert.Contains(Logs, entry => entry.Message == "2 clicks were skipped because their link no longer exists");
        Assert.DoesNotContain(Logs, entry => entry.Level >= LogLevel.Error);
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailedBatchIsLoggedWithoutClickDataAndTheWriterKeepsRunning()
    {
        var store = new FailingFirstBatchStore();
        await BuildAsync(services => services.AddScoped<IClickEventStore>(provider =>
            store.Wrap(ActivatorUtilities.CreateInstance<ClickEventStore>(provider))));
        var link = await AddLinkAsync("flaky");
        var writer = await StartWriterAsync();

        Record(Click(link, RawReferer, RawUserAgent));
        await Eventually.AssertAsync(() => Task.FromResult(store.Calls == 1), "the first batch attempted");
        Record(Click(link, RawReferer, RawUserAgent));

        await Eventually.AssertAsync(async () => await EventCountAsync() == 1, "the next batch stored");
        Assert.Equal(1, await ClickCountAsync(link));
        var failure = Assert.Single(Logs, entry => entry.Level == LogLevel.Error);
        Assert.Equal("Storing a batch of 1 clicks failed; the batch was discarded", failure.Message);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.All(Logs, entry =>
        {
            Assert.DoesNotContain("/article", entry.FullText, StringComparison.Ordinal);
            Assert.DoesNotContain("u=42", entry.FullText, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", entry.FullText, StringComparison.Ordinal);
            Assert.DoesNotContain("Mozilla", entry.FullText, StringComparison.Ordinal);
        });
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BatchesHoldAtMostFiveHundredEvents()
    {
        var store = new RecordingStore();
        await BuildAsync(services => services.AddScoped<IClickEventStore>(provider =>
            store.Wrap(ActivatorUtilities.CreateInstance<ClickEventStore>(provider))));
        var link = await AddLinkAsync("busy");
        Record(Enumerable.Range(0, 1_200).Select(_ => Click(link)).ToArray());

        var writer = await StartWriterAsync();

        await Eventually.AssertAsync(async () => await ClickCountAsync(link) == 1_200, "all 1 200 clicks stored");
        Assert.Equal([500, 500, 200], store.BatchSizes);
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DroppedClicksAreLogged()
    {
        await BuildAsync(services => services.Configure<ClickRecordingOptions>(options => options.QueueCapacity = 2));
        var link = await AddLinkAsync("overflow");
        Record(Click(link), Click(link), Click(link), Click(link), Click(link));

        var writer = await StartWriterAsync();

        await Eventually.AssertAsync(
            () => Task.FromResult(Logs.Any(entry => entry.Message == "3 queued clicks were dropped because the click queue was full")),
            "the drop count logged");
        Assert.Equal(2, await ClickCountAsync(link));
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StoppingFlushesQueuedClicks()
    {
        await BuildAsync();
        var link = await AddLinkAsync("closing");
        var writer = await StartWriterAsync();

        Record(Enumerable.Range(0, 50).Select(_ => Click(link)).ToArray());
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(50, await ClickCountAsync(link));
        Assert.Equal(50, await EventCountAsync());
    }

    [Fact]
    public async Task StoppingFinishesTheBatchInProgressAndTheQueuedClicks()
    {
        await BuildAsync(services =>
        {
            services.AddSingleton<GatedStore>();
            services.AddScoped<IClickEventStore>(provider => provider.GetRequiredService<GatedStore>()
                .Wrap(ActivatorUtilities.CreateInstance<ClickEventStore>(provider)));
        });
        var link = await AddLinkAsync("draining");
        var store = _services.GetRequiredService<GatedStore>();
        var writer = await StartWriterAsync();

        // One click is being written (held at the gate) while 50 more wait in the queue.
        Record(Click(link));
        await Eventually.AssertAsync(() => Task.FromResult(store.Waiting), "the first batch held at the gate");
        Record(Enumerable.Range(0, 50).Select(_ => Click(link)).ToArray());

        var stopping = writer.StopAsync(CancellationToken.None);
        store.Open();
        await stopping;

        Assert.Equal(51, await ClickCountAsync(link));
        Assert.Equal(51, await EventCountAsync());
    }

    [Fact]
    public async Task StoppingGivesUpAfterTheFlushTimeoutWhenStorageHangs()
    {
        await BuildAsync(services =>
        {
            services.Configure<ClickRecordingOptions>(options => options.ShutdownFlushTimeout = TimeSpan.FromMilliseconds(200));
            services.AddSingleton<HangingStore>();
            services.AddScoped<IClickEventStore>(provider => provider.GetRequiredService<HangingStore>());
        });
        var writer = await StartWriterAsync();
        var store = _services.GetRequiredService<HangingStore>();

        // One click stuck in storage and one still queued: both are lost, and reported once, together.
        Record(Click(1));
        await Eventually.AssertAsync(() => Task.FromResult(store.Started), "a batch in progress");
        Record(Click(1));

        var stopping = writer.StopAsync(CancellationToken.None);

        Assert.Same(stopping, await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(5))));
        var lost = Assert.Single(Logs, entry => entry.Message.EndsWith("not stored before shutdown", StringComparison.Ordinal));
        Assert.Equal("2 clicks were not stored before shutdown", lost.Message);
    }

    private async Task BuildAsync(Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = $"Data Source={_databasePath}" })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<CapturingLoggerProvider>()
            .AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<CapturingLoggerProvider>())
            .AddLogging()
            .AddInfrastructure();
        configure?.Invoke(services);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await Db(scope).Database.MigrateAsync();
    }

    private async Task<IHostedService> StartWriterAsync()
    {
        var writer = _services.GetServices<IHostedService>().OfType<ClickEventWriter>().Single();
        await writer.StartAsync(CancellationToken.None);
        return writer;
    }

    private void Record(params ClickEvent[] clicks)
    {
        var recorder = _services.GetRequiredService<ChannelClickRecorder>();
        Assert.All(clicks, click => Assert.True(recorder.TryRecord(click)));
    }

    private static ClickEvent Click(long linkId, string? referer = null, string? userAgent = null) =>
        ClickEvent.Create(linkId, CreatedAt.AddMinutes(1), referer, userAgent);

    private async Task<long> AddLinkAsync(string alias)
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IShortLinkRepository>();
        Assert.True(await repository.TryAddAsync(
            ShortLink.CreateWithCustomAlias(alias, "https://example.com/a", CreatedAt), CancellationToken.None));
        return (await repository.FindByNormalizedCodeAsync(alias, CancellationToken.None))!.Id;
    }

    private async Task<long> ClickCountAsync(long linkId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await Db(scope).ShortLinks.Where(link => link.Id == linkId).Select(link => link.ClickCount).SingleAsync();
    }

    private async Task<int> EventCountAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await Db(scope).ClickEvents.CountAsync();
    }

    private IReadOnlyList<CapturingLoggerProvider.LogEntry> Logs => _services.GetRequiredService<CapturingLoggerProvider>().Entries;

    private static AppDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();

    /// <summary>Throws on the first batch only, as a transient storage failure would.</summary>
    private sealed class FailingFirstBatchStore
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IClickEventStore Wrap(IClickEventStore inner) => new Store(this, inner);

        private sealed class Store(FailingFirstBatchStore owner, IClickEventStore inner) : IClickEventStore
        {
            public Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken) =>
                Interlocked.Increment(ref owner._calls) == 1
                    ? Task.FromException<int>(new InvalidOperationException("database is locked"))
                    : inner.SaveAsync(clicks, cancellationToken);
        }
    }

    /// <summary>Records each batch's size, then stores it for real.</summary>
    private sealed class RecordingStore
    {
        private readonly ConcurrentQueue<int> _sizes = new();

        public int[] BatchSizes => [.. _sizes];

        public IClickEventStore Wrap(IClickEventStore inner) => new Store(this, inner);

        private sealed class Store(RecordingStore owner, IClickEventStore inner) : IClickEventStore
        {
            public Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken)
            {
                owner._sizes.Enqueue(clicks.Count);
                return inner.SaveAsync(clicks, cancellationToken);
            }
        }
    }

    /// <summary>Holds every batch until <see cref="Open"/> is called, then stores it for real.</summary>
    private sealed class GatedStore
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _waiting;

        public bool Waiting => _waiting;

        public void Open() => _gate.TrySetResult();

        public IClickEventStore Wrap(IClickEventStore inner) => new Store(this, inner);

        private sealed class Store(GatedStore owner, IClickEventStore inner) : IClickEventStore
        {
            public async Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken)
            {
                owner._waiting = true;
                await owner._gate.Task.WaitAsync(cancellationToken);
                return await inner.SaveAsync(clicks, cancellationToken);
            }
        }
    }

    /// <summary>Never finishes until cancelled, like a database that stopped answering.</summary>
    private sealed class HangingStore : IClickEventStore
    {
        private volatile bool _started;

        public bool Started => _started;

        public async Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken)
        {
            _started = true;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}