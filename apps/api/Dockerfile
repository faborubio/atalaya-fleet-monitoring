# Imagen del API .NET 8 (Minimal API + SignalR) para Cloud Run (G5, ADR-013).
# Contexto de build = raíz del repo:  docker build -f apps/api/Dockerfile -t atalaya-api .
# Multi-stage: restore cacheado por csproj, luego publish; runtime aspnet (más liviano que el SDK).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia primero los csproj + nuget.config para cachear el restore (no se invalida al cambiar código).
COPY nuget.config ./
COPY libs/contracts/*.csproj libs/contracts/
COPY libs/persistence/*.csproj libs/persistence/
COPY libs/realtime/*.csproj libs/realtime/
COPY apps/api/*.csproj apps/api/
RUN dotnet restore apps/api/Atalaya.Api.csproj

# Luego el código y publish (sin volver a restaurar).
COPY libs/ libs/
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/Atalaya.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Cloud Run enruta al puerto 8080; Kestrel escucha en todas las interfaces.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Sin appsettings.Development (ASPNETCORE_ENVIRONMENT por defecto = Production); la config la inyecta
# Cloud Run por variables de entorno (Terraform): Telemetry__Transport, Gcp__*, Auth__*, Cors__*, etc.
ENTRYPOINT ["dotnet", "Atalaya.Api.dll"]
