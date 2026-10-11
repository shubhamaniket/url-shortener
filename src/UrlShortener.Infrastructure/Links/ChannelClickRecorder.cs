using System.Threading.Channels;

using Microsoft.Extensions.Options;

using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Links;

/// <summary>
/// Queues click events in memory for the background writer, so a redirect costs one in-memory
/// enqueue and no database I/O (research R4, constitution V). The queue is bounded: when it is
/// full the oldest event is dropped and counted, never the redirect delayed. Singleton.
/// </summary>
public sealed class ChannelClickRecorder : IClickRecorder
{
    private readonly Channel<ClickEvent> _channel;
    private long _droppedCount;

    public ChannelClickRecorder(IOptions<ClickRecordingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capacity = options.Value.QueueCapacity;
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1, nameof(ClickRecordingOptions.QueueCapacity));

        _channel = Channel.CreateBounded<ClickEvent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => Interlocked.Increment(ref _droppedCount));
    }

    /// <summary>The writer's side of the queue; there is exactly one reader.</summary>
    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    /// <summary>
    /// Queues the event without waiting. Returns <c>false</c> only once the queue has been closed;
    /// a full queue makes room by dropping its oldest event.
    /// </summary>
    public bool TryRecord(ClickEvent clickEvent)
    {
        ArgumentNullException.ThrowIfNull(clickEvent);
        return _channel.Writer.TryWrite(clickEvent);
    }

    /// <summary>Returns the number of events dropped since the last call and resets it (read by the writer to log drops).</summary>
    public long TakeDroppedCount() => Interlocked.Exchange(ref _droppedCount, 0);
}