using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Application.Abstractions;
using UrlShortener.Infrastructure.Links;
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
        services.AddSingleton<IShortCodeGenerator, Base62CodeGenerator>();

        // Click recording (feature 002): redirects queue clicks in memory; the hosted writer stores
        // them in batches. One recorder instance serves both sides.
        services.AddOptions<ClickRecordingOptions>();
        services.AddSingleton<ChannelClickRecorder>();
        services.AddSingleton<IClickRecorder>(provider => provider.GetRequiredService<ChannelClickRecorder>());
        services.AddScoped<IClickEventStore, ClickEventStore>();
        services.AddHostedService<ClickEventWriter>();
        services.AddScoped<IClickStatsReader, ClickStatsReader>();

        return services;
    }
}