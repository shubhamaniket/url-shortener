namespace UrlShortener.Infrastructure.Links;

/// <summary>Settings bound from the <c>ClickRecording</c> configuration section (research R4, R5).</summary>
public sealed class ClickRecordingOptions
{
    public const string SectionName = "ClickRecording";

    /// <summary>
    /// Clicks held in memory while waiting to be stored. When full, the oldest queued click is
    /// dropped so the redirect never waits (engineer-approved decision R4).
    /// </summary>
    public int QueueCapacity { get; set; } = 10_000;
}