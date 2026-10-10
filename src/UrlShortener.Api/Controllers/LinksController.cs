using Microsoft.AspNetCore.Mvc;

using UrlShortener.Api.Contracts;
using UrlShortener.Application.Links;

namespace UrlShortener.Api.Controllers;

/// <summary>Create short links. Thin: maps HTTP to <see cref="ILinkService"/>; errors are handled globally.</summary>
[ApiController]
[Route("api/links")]
public sealed class LinksController(ILinkService linkService) : ControllerBase
{
    /// <summary>Create a short link with a generated code or the requested custom alias.</summary>
    [HttpPost]
    [ProducesResponseType<LinkResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<LinkResponse>> Create(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var details = await linkService.CreateAsync(
            new CreateLinkCommand(request.Url!, request.CustomAlias), cancellationToken);

        return Created(new Uri($"/api/links/{details.Code}", UriKind.Relative), LinkResponse.From(details));
    }
}