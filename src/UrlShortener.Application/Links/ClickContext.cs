namespace UrlShortener.Application.Links;

/// <summary>
/// What the redirect request says about a click: the raw <c>Referer</c> and <c>User-Agent</c> header
/// values only, never the client's IP address. The Domain reduces both before anything is stored (FR-007–FR-010).
/// </summary>
public sealed record ClickContext(string? Referrer, string? UserAgent)
{
    /// <summary>A click with neither header.</summary>
    public static ClickContext None { get; } = new(null, null);
}