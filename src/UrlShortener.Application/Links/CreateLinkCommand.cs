namespace UrlShortener.Application.Links;

public sealed record CreateLinkCommand(string Url, string? CustomAlias);