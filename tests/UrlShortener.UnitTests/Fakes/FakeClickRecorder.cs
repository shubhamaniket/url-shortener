using UrlShortener.Application.Abstractions;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>Counts recorded clicks, or throws the given exception when one is set.</summary>
internal sealed class FakeClickRecorder(Exception? failWith = null) : IClickRecorder
{
    public List<long> RecordedLinkIds { get; } = [];

    public Task RecordClickAsync(long linkId, CancellationToken cancellationToken)
    {
        RecordedLinkIds.Add(linkId);
        return failWith is null ? Task.CompletedTask : Task.FromException(failWith);
    }
}