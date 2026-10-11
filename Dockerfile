# syntax=docker/dockerfile:1
# One image: the web UI is built first and served by the API from wwwroot (ADR-010).

# 1. Web UI → src/UrlShortener.Api/wwwroot
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2. API (Release), including the built UI
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY src/UrlShortener.Domain/UrlShortener.Domain.csproj src/UrlShortener.Domain/
COPY src/UrlShortener.Application/UrlShortener.Application.csproj src/UrlShortener.Application/
COPY src/UrlShortener.Infrastructure/UrlShortener.Infrastructure.csproj src/UrlShortener.Infrastructure/
COPY src/UrlShortener.Api/UrlShortener.Api.csproj src/UrlShortener.Api/
RUN dotnet restore src/UrlShortener.Api/UrlShortener.Api.csproj
COPY src/ src/
COPY --from=web /src/src/UrlShortener.Api/wwwroot src/UrlShortener.Api/wwwroot
RUN dotnet publish src/UrlShortener.Api/UrlShortener.Api.csproj -c Release -o /app --no-restore

# 3. Runtime: unprivileged user, data on a volume, migrations applied on start
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN mkdir /data && chown "$APP_UID" /data
COPY --from=build /app ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/data/urlshortener.db" \
    Database__MigrateOnStartup=true
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "UrlShortener.Api.dll"]
