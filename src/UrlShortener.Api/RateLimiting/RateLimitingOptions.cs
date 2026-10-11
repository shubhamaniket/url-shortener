namespace UrlShortener.Api.RateLimiting;

/// <summary>
/// Per-client request budgets, bound from the <c>RateLimiting</c> section (feature 003, research R2,
/// R5). Defaults are the values confirmed in Clarification 3.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Creating links: fixed window. Default 10 per 60 seconds.</summary>
    public FixedWindowBudget Create { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Following short links: token bucket. Default bursts of 60, refilled 2 per second (120 per minute).</summary>
    public TokenBucketBudget Redirect { get; set; } = new() { BurstLimit = 60, TokensPerSecond = 2 };

    /// <summary>Link details and statistics: fixed window. Default 60 per 60 seconds.</summary>
    public FixedWindowBudget Read { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    /// <summary>Returns one message per invalid setting; empty when the settings are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        AtLeastOne(Create.PermitLimit, $"{nameof(Create)}:{nameof(FixedWindowBudget.PermitLimit)}");
        AtLeastOne(Create.WindowSeconds, $"{nameof(Create)}:{nameof(FixedWindowBudget.WindowSeconds)}");
        AtLeastOne(Redirect.BurstLimit, $"{nameof(Redirect)}:{nameof(TokenBucketBudget.BurstLimit)}");
        AtLeastOne(Redirect.TokensPerSecond, $"{nameof(Redirect)}:{nameof(TokenBucketBudget.TokensPerSecond)}");
        AtLeastOne(Read.PermitLimit, $"{nameof(Read)}:{nameof(FixedWindowBudget.PermitLimit)}");
        AtLeastOne(Read.WindowSeconds, $"{nameof(Read)}:{nameof(FixedWindowBudget.WindowSeconds)}");
        return errors;

        void AtLeastOne(int value, string setting)
        {
            if (value < 1)
            {
                errors.Add($"{SectionName}:{setting} must be at least 1.");
            }
        }
    }
}

public sealed class FixedWindowBudget
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}

public sealed class TokenBucketBudget
{
    /// <summary>Most requests accepted at once (bucket size).</summary>
    public int BurstLimit { get; set; }

    /// <summary>Requests added back to the bucket each second.</summary>
    public int TokensPerSecond { get; set; }
}