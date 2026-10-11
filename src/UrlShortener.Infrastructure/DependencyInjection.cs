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
        services.AddScoped<IClickRecorder, DbClickRecorder>();

        // Background click pipeline (feature 002). Built and running, but nothing is queued until
        // the redirect switches from DbClickRecorder to the channel recorder.
        services.AddOptions<ClickRecordingOptions>();
        services.AddSingleton<ChannelClickRecorder>();
        services.AddScoped<IClickEventStore, ClickEventStore>();
        services.AddHostedService<ClickEventWriter>();

        return services;
    }
}