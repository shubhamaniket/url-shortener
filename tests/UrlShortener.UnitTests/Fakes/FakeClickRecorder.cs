using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Links;

namespace UrlShortener.UnitTests.Fakes;

/// <summary>Keeps recorded clicks, or throws the given exception when one is set.</summary>
internal sealed class FakeClickRecorder(Exception? failWith = null) : IClickRecorder
{
    public List<ClickEvent> Recorded { get; } = [];

    public IEnumerable<long> RecordedLinkIds => Recorded.Select(click => click.ShortLinkId);

    public bool TryRecord(ClickEvent clickEvent)
    {
        Recorded.Add(clickEvent);
        return failWith is null ? true : throw failWith;
    }
}