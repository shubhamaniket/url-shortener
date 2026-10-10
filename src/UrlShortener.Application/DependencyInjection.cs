using Microsoft.Extensions.DependencyInjection;

using UrlShortener.Application.Links;

namespace UrlShortener.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILinkService, LinkService>();
        return services;
    }
}