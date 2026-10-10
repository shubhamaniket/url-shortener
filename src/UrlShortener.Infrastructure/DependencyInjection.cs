using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Application.Abstractions;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure;

public static class DependencyInjection
{
    public const string DefaultConnectionString = "Data Source=urlshortener.db";

    /// <remarks>
    /// The connection string is read when the context is first created, not at registration, so
    /// configuration added after service registration (e.g. by test hosts) is honoured.
    /// </remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((provider, options) => options.UseSqlite(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("Default") ?? DefaultConnectionString));
        services.AddScoped<IShortLinkRepository, ShortLinkRepository>();

        return services;
    }
}