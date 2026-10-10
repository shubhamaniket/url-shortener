using Microsoft.Extensions.Options;

using UrlShortener.Api.Configuration;
using UrlShortener.Application.Options;
using UrlShortener.Infrastructure;
using UrlShortener.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddOptions<ShortLinkOptions>()
    .Bind(builder.Configuration.GetSection(ShortLinkOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ShortLinkOptions>, ShortLinkOptionsValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No UseHttpsRedirection: TLS and the HTTP -> HTTPS redirect belong to the hosting proxy (research R13).

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program { }