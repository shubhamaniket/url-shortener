namespace UrlShortener.Domain.Links;

/// <summary>A request field broke a link rule; reported to clients as a 400 with field errors.</summary>
public sealed class LinkValidationException : Exception
{
    public LinkValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    /// <summary>The request field name as clients see it, e.g. <c>url</c> or <c>customAlias</c>.</summary>
    public string Field { get; }
}