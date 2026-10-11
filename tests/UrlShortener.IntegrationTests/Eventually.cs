using System.Diagnostics;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Polls until a condition holds. Clicks are stored in the background, so counts are eventually
/// consistent; 5 seconds is the guarantee in FR-012.
/// </summary>
internal static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task AssertAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!await condition())
        {
            if (stopwatch.Elapsed > (timeout ?? DefaultTimeout))
            {
                Assert.Fail($"Not met within {(timeout ?? DefaultTimeout).TotalSeconds} s: {because}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}