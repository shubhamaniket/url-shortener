using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UrlShortener.Domain.Links;

namespace UrlShortener.Infrastructure.Links;

/// <summary>
/// Stores queued click events in the background (research R5). It writes as soon as events arrive,
/// in batches of up to <see cref="MaxBatchSize"/>; a failed batch is logged and discarded (at-most-once,
/// ADR-005) and the writer carries on. On shutdown it keeps writing queued events for up to
/// <see cref="ClickRecordingOptions.ShutdownFlushTimeout"/>. Logs carry counts only, never click data.
/// </summary>
public sealed partial class ClickEventWriter(
    ChannelClickRecorder recorder,
    IServiceScopeFactory scopeFactory,
    IOptions<ClickRecordingOptions> options,
    ILogger<ClickEventWriter> logger) : BackgroundService
{
    public const int MaxBatchSize = 500;

    private readonly TimeSpan _shutdownFlushTimeout = options.Value.ShutdownFlushTimeout;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Writes are not cancelled by the stop request itself, so a batch in progress and the queued
        // events can still be stored; the budget cuts them off once the flush time is over.
        using var writeBudget = new CancellationTokenSource();
        await using var onStop = stoppingToken.Register(() => writeBudget.CancelAfter(_shutdownFlushTimeout));

        try
        {
            while (await recorder.Reader.WaitToReadAsync(stoppingToken))
            {
                await WriteQueuedAsync(writeBudget.Token);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: fall through to the final flush.
        }

        await WriteQueuedAsync(writeBudget.Token);
    }

    private async Task WriteQueuedAsync(CancellationToken cancellationToken)
    {
        var batch = new List<ClickEvent>(MaxBatchSize);
        while (!cancellationToken.IsCancellationRequested)
        {
            batch.Clear();
            while (batch.Count < MaxBatchSize && recorder.Reader.TryRead(out var click))
            {
                batch.Add(click);
            }

            if (batch.Count == 0)
            {
                return;
            }

            await WriteBatchAsync(batch, cancellationToken);
        }

        if (recorder.Reader.Count > 0)
        {
            LogNotStoredAtShutdown(logger, recorder.Reader.Count);
        }
    }

    private async Task WriteBatchAsync(List<ClickEvent> batch, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var skipped = await scope.ServiceProvider.GetRequiredService<IClickEventStore>().SaveAsync(batch, cancellationToken);
            if (skipped > 0)
            {
                LogClicksForDeletedLinks(logger, skipped);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogNotStoredAtShutdown(logger, batch.Count);
        }
        catch (Exception ex)
        {
            // Any storage failure: report it and keep going; analytics must never stop (FR-014).
            LogBatchFailed(logger, ex, batch.Count);
        }

        var dropped = recorder.TakeDroppedCount();
        if (dropped > 0)
        {
            LogClicksDropped(logger, dropped);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Storing a batch of {Count} clicks failed; the batch was discarded")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} queued clicks were dropped because the click queue was full")]
    private static partial void LogClicksDropped(ILogger logger, long count);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} clicks were skipped because their link no longer exists")]
    private static partial void LogClicksForDeletedLinks(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} clicks were not stored before shutdown")]
    private static partial void LogNotStoredAtShutdown(ILogger logger, int count);
}