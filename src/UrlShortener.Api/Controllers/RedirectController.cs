using Microsoft.AspNetCore.Mvc;

using UrlShortener.Application.Links;

namespace UrlShortener.Api.Controllers;

/// <summary>
/// The redirect hot path, kept apart from the API controller (own route, future rate-limit policy).
/// Hidden from Swagger; documented in contracts/openapi.yaml.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class RedirectController(ILinkService linkService) : ControllerBase
{
    /// <summary>
    /// 302 to the original URL, or 404. The route constraint rejects malformed codes before any
    /// lookup; literal routes such as <c>/health</c> and <c>/api/...</c> take precedence.
    /// </summary>
    [HttpGet("{code:regex(^[[A-Za-z0-9_-]]{{3,30}}$)}")]
    public async Task<IActionResult> Follow(string code, CancellationToken cancellationToken)
    {
        var resolution = await linkService.ResolveForRedirectAsync(code, cancellationToken);
        if (!resolution.IsFound)
        {
            // Expired links cannot be created through the API yet; feature 002 task T008 adds
            // expiresAt and maps Expired to 410 Gone.
            return NotFound();
        }

        // Temporary (302) and not cacheable, so every visit reaches the service and is counted (FR-013).
        Response.Headers.CacheControl = "no-store";
        return Redirect(resolution.Url);
    }
}