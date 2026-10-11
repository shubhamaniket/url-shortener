using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using UrlShortener.Api.RateLimiting;
using UrlShortener.Application.Links;

namespace UrlShortener.Api.Controllers;

/// <summary>
/// The redirect hot path, kept apart from the API controller (own route, own rate-limit policy).
/// Every outcome (302, 404, 410) counts against the client's redirect budget (feature 003, FR-008).
/// Hidden from Swagger; documented in contracts/openapi.yaml.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(RateLimitPolicies.Redirect)]
public sealed class RedirectController(ILinkService linkService) : ControllerBase
{
    /// <summary>
    /// 302 to the original URL, 404 for an unknown code, or 410 for an expired link. The route constraint rejects malformed codes before any
    /// lookup; literal routes such as <c>/health</c> and <c>/api/...</c> take precedence.
    /// </summary>
    [HttpGet("{code:regex(^[[A-Za-z0-9_-]]{{3,30}}$)}")]
    public async Task<IActionResult> Follow(string code, CancellationToken cancellationToken)
    {
        // Only these two headers reach the Application layer; the client's IP address never does (FR-010).
        var click = new ClickContext(Request.Headers.Referer.FirstOrDefault(), Request.Headers.UserAgent.FirstOrDefault());
        var resolution = await linkService.ResolveForRedirectAsync(code, click, cancellationToken);
        if (resolution.Outcome == RedirectOutcome.Expired)
        {
            // Gone for good, unless the alias is later reclaimed; never cached, so a reclaimed code
            // is followed to its new destination (FR-004, FR-006a).
            Response.Headers.CacheControl = "no-store";
            return Problem(
                type: "https://tools.ietf.org/html/rfc9110#section-15.5.11",
                title: "Link expired",
                statusCode: StatusCodes.Status410Gone,
                detail: "This short link has expired.");
        }

        if (!resolution.IsFound)
        {
            return NotFound();
        }

        // Temporary (302) and not cacheable, so every visit reaches the service and is counted (FR-013).
        Response.Headers.CacheControl = "no-store";
        return Redirect(resolution.Url);
    }
}