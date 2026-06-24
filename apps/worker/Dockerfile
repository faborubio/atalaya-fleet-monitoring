# Imagen del Worker .NET 8 (consumidor Pub/Sub pull + read models + camino frío) para Cloud Run (G5).
# Contexto de build = raíz del repo:  docker build -f apps/worker/Dockerfile -t atalaya-worker .
# Corre always-on en Cloud Run (min-instances=1, CPU siempre asignada) porque el pull es continuo.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nuget.config ./
COPY libs/contracts/*.csproj libs/contracts/
COPY libs/persistence/*.csproj libs/persistence/
COPY libs/realtime/*.csproj libs/realtime/
COPY apps/worker/*.csproj apps/worker/
RUN dotnet restore apps/worker/Atalaya.Worker.csproj

COPY libs/ libs/
COPY apps/worker/ apps/worker/
RUN dotnet publish apps/worker/Atalaya.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Un Worker no sirve ASP.NET; el runtime base basta (el health usa HttpListener de System.Net).
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Cloud Run inyecta PORT (8080); WorkerHealthService escucha en http://+:$PORT para su sonda de arranque.
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Atalaya.Worker.dll"]
