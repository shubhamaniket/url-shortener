using Microsoft.Extensions.Options;

using UrlShortener.Domain.Links;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.UnitTests.Infrastructure;

public class ChannelClickRecorderTests
{
    private static readonly DateTime Start = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DefaultCapacityIsTenThousand()
    {
        Assert.Equal(10_000, new ClickRecordingOptions().QueueCapacity);
    }

    [Fact]
    public void QueuedEventsAreReadInOrder()
    {
        var recorder = CreateRecorder(capacity: 10);
        var events = Enumerable.Range(1, 5).Select(Click).ToList();

        Assert.All(events, click => Assert.True(recorder.TryRecord(click)));

        Assert.Equal(events, ReadAll(recorder));
        Assert.Equal(0, recorder.TakeDroppedCount());
    }

    [Fact]
    public void FullQueueKeepsTheNewestEventsAndCountsDrops()
    {
        var recorder = CreateRecorder(capacity: 3);
        var events = Enumerable.Range(1, 5).Select(Click).ToList();

        Assert.All(events, click => Assert.True(recorder.TryRecord(click)));

        Assert.Equal(events[2..], ReadAll(recorder));
        Assert.Equal(2, recorder.TakeDroppedCount());
    }

    [Fact]
    public void TakingTheDroppedCountResetsIt()
    {
        var recorder = CreateRecorder(capacity: 1);
        recorder.TryRecord(Click(1));
        recorder.TryRecord(Click(2));

        Assert.Equal(1, recorder.TakeDroppedCount());
        Assert.Equal(0, recorder.TakeDroppedCount());
    }

    [Fact]
    public async Task ConcurrentWritersLoseNothingBelowCapacity()
    {
        const int Writers = 8;
        const int PerWriter = 1_000;
        var recorder = CreateRecorder(capacity: Writers * PerWriter);
        using var start = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, Writers).Select(writer => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < PerWriter; i++)
            {
                Assert.True(recorder.TryRecord(Click((writer * PerWriter) + i + 1)));
            }
        })).ToList();
        start.Set();
        await Task.WhenAll(tasks);

        var read = ReadAll(recorder);
        Assert.Equal(Writers * PerWriter, read.Count);
        Assert.Equal(Writers * PerWriter, read.Select(click => click.ShortLinkId).Distinct().Count());
        Assert.Equal(0, recorder.TakeDroppedCount());
    }

    [Fact]
    public void RecordingNeverWaitsEvenWhenNothingReads()
    {
        var recorder = CreateRecorder(capacity: 2);

        // Nothing drains the queue: every call must still return at once.
        for (var i = 1; i <= 10_000; i++)
        {
            Assert.True(recorder.TryRecord(Click(i)));
        }

        Assert.Equal(9_998, recorder.TakeDroppedCount());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CapacityMustBePositive(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRecorder(capacity));
    }

    private static ChannelClickRecorder CreateRecorder(int capacity) =>
        new(Options.Create(new ClickRecordingOptions { QueueCapacity = capacity }));

    private static ClickEvent Click(int linkId) => ClickEvent.Create(linkId, Start.AddSeconds(linkId), null, null);

    private static List<ClickEvent> ReadAll(ChannelClickRecorder recorder)
    {
        var events = new List<ClickEvent>();
        while (recorder.Reader.TryRead(out var click))
        {
            events.Add(click);
        }

        return events;
    }
}