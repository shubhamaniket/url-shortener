using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Links;
using UrlShortener.Infrastructure.Persistence;

using Xunit.Abstractions;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Redirects with analytics impaired (feature 002, SC-001, SC-002): blocked storage and failing
/// storage must never slow down or fail a redirect. In-process evidence; real latency is measured
/// in T018.
/// </summary>
public sealed class AnalyticsResilienceTests(ApiFactory factory, ITestOutputHelper output) : IClassFixture<ApiFactory>
{
    private const int Redirects = 50;
    private const int QueueCapacity = 10;
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task RedirectsCompleteWhileTheWriterIsBlockedAndTheNewestClicksAreKept()
    {
        var code = await CreateLinkAsync("https://example.com/under-load");
        var gate = new Gate();
        using var logs = new CapturingLoggerProvider();
        await using var impaired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"{ClickRecordingOptions.SectionName}:{nameof(ClickRecordingOptions.QueueCapacity)}", $"{QueueCapacity}");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services => services.AddScoped<IClickEventStore>(provider =>
                gate.Wrap(ActivatorUtilities.CreateInstance<ClickEventStore>(provider))));
        });
        var client = impaired.CreateClient(NoRedirects);

        try
        {
            // The first click reaches storage and blocks the writer there; it also warms up the host.
            Assert.Equal(HttpStatusCode.Found, (await FollowAsync(client, code, 0)).StatusCode);
            await Eventually.AssertAsync(() => Task.FromResult(gate.Waiting), "the writer blocked in storage");

            var durations = new List<TimeSpan>();
            for (var i = 1; i < Redirects; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var response = await FollowAsync(client, code, i);
                durations.Add(stopwatch.Elapsed);

                Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            }

            // The non-timing proof: every redirect finished while the writer was still blocked.
            Assert.False(gate.IsOpen);
            Assert.Equal(0, await StoredClickCountAsync(code));

            var p95 = durations.Order().ElementAt((int)Math.Ceiling(durations.Count * 0.95) - 1);
            output.WriteLine($"Writer blocked: {durations.Count} redirects, p95 {p95.TotalMilliseconds:F2} ms, max {durations.Max().TotalMilliseconds:F2} ms");
            Assert.True(p95 < TimeSpan.FromMilliseconds(100), $"p95 redirect time {p95.TotalMilliseconds:F1} ms with the writer blocked");
        }
        finally
        {
            gate.Open();
        }

        // Once storage recovers: the blocked click plus the newest QueueCapacity clicks are stored; the
        // oldest queued ones were dropped and the drop is reported.
        var expectedStored = 1 + QueueCapacity;
        await Eventually.AssertAsync(async () => await StoredClickCountAsync(code) == expectedStored, $"{expectedStored} clicks stored");
        var expectedHosts = Enumerable.Range(Redirects - QueueCapacity, QueueCapacity).Prepend(0).Select(Host).Order(StringComparer.Ordinal);
        Assert.Equal(expectedHosts, (await StoredHostsAsync(code)).Order(StringComparer.Ordinal));
        var dropped = Redirects - expectedStored;
        await Eventually.AssertAsync(
            () => Task.FromResult(logs.Entries.Any(entry =>
                entry.Level == LogLevel.Warning && entry.Message == $"{dropped} queued clicks were dropped because the click queue was full")),
            "the drop count logged");
    }

    [Fact]
    public async Task EveryRedirectSucceedsWhileStorageKeepsFailing()
    {
        var code = await CreateLinkAsync("https://example.com/storage-down");
        using var logs = new CapturingLoggerProvider();
        await using var impaired = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services => services.AddScoped<IClickEventStore, AlwaysFailingStore>());
        });
        var client = impaired.CreateClient(NoRedirects);

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < Redirects; i++)
        {
            responses.Add(await FollowAsync(client, code, i));
        }

        Assert.All(responses, response =>
        {
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("https://example.com/storage-down", response.Headers.Location?.OriginalString);
        });
        await Eventually.AssertAsync(
            () => Task.FromResult(logs.Entries.Any(entry => entry.Level == LogLevel.Error)),
            "storage failures logged");
        Assert.Equal(0, await StoredClickCountAsync(code));
    }

    private static string Host(int index) => $"r{index}.example";

    private static async Task<HttpResponseMessage> FollowAsync(HttpClient client, string code, int index)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/{code}", UriKind.Relative));
        request.Headers.Referrer = new Uri($"https://{Host(index)}/page");
        return await client.SendAsync(request);
    }

    private async Task<string> CreateLinkAsync(string url)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(new Uri("/api/links", UriKind.Relative), new { url });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    private async Task<long> StoredClickCountAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ShortLinks
            .Where(link => link.Code == code).Select(link => link.ClickCount).SingleAsync();
    }

    private async Task<List<string>> StoredHostsAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var linkId = await db.ShortLinks.Where(link => link.Code == code).Select(link => link.Id).SingleAsync();
        return await db.ClickEvents.Where(click => click.ShortLinkId == linkId).Select(click => click.ReferrerHost!).ToListAsync();
    }

    /// <summary>Holds every batch in storage until opened, like a database that has stopped answering.</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _waiting;

        public bool Waiting => _waiting;

        public bool IsOpen => _open.Task.IsCompleted;

        public void Open() => _open.TrySetResult();

        public IClickEventStore Wrap(IClickEventStore inner) => new Store(this, inner);

        private sealed class Store(Gate gate, IClickEventStore inner) : IClickEventStore
        {
            public async Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken)
            {
                gate._waiting = true;
                await gate._open.Task.WaitAsync(cancellationToken);
                return await inner.SaveAsync(clicks, cancellationToken);
            }
        }
    }

    private sealed class AlwaysFailingStore : IClickEventStore
    {
        public Task<int> SaveAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database is locked");
    }
}