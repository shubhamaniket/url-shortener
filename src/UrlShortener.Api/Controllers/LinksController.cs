using Microsoft.AspNetCore.Mvc;

using UrlShortener.Api.Contracts;
using UrlShortener.Application.Links;

namespace UrlShortener.Api.Controllers;

/// <summary>Create short links and read their details. Thin: maps HTTP to <see cref="ILinkService"/>; errors are handled globally.</summary>
[ApiController]
[Route("api/links")]
public sealed class LinksController(ILinkService linkService) : ControllerBase
{
    /// <summary>Create a short link with a generated code or the requested custom alias, optionally expiring.</summary>
    [HttpPost]
    [ProducesResponseType<LinkResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<LinkResponse>> Create(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var details = await linkService.CreateAsync(
            new CreateLinkCommand(request.Url!, request.CustomAlias, request.ExpiresAt), cancellationToken);

        return Created(new Uri($"/api/links/{details.Code}", UriKind.Relative), LinkResponse.From(details));
    }

    /// <summary>Get a link's details and total click count, also after it has expired. Public: knowing the code is enough.</summary>
    [HttpGet("{code}")]
    [ProducesResponseType<LinkResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<LinkResponse>> Get(string code, CancellationToken cancellationToken)
    {
        var details = await linkService.GetDetailsAsync(code, cancellationToken);
        return details is null ? NotFound() : LinkResponse.From(details);
    }

    /// <summary>Get click statistics: all-time total, clicks per UTC day for the last 30 days, and the top 5 referrers.</summary>
    /// <remarks>Public like details, also for expired links. Reading statistics is not a click.</remarks>
    [HttpGet("{code}/stats")]
    [ProducesResponseType<LinkStatsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<LinkStatsResponse>> GetStats(string code, CancellationToken cancellationToken)
    {
        var statistics = await linkService.GetStatsAsync(code, cancellationToken);
        return statistics is null ? NotFound() : LinkStatsResponse.From(statistics);
    }
}