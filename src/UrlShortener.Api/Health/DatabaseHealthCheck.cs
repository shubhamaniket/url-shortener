using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api.Health;

/// <summary>
/// Healthy only when the database can be opened (FR-019). Lives in the Api project because the
/// health check abstractions ship with ASP.NET Core; putting it in Infrastructure would need a new
/// package (research R10, constitution VI).
/// </summary>
internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database cannot be reached.");
}