using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atalaya.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el acceso a Postgres. Toma la cadena de ConnectionStrings:Postgres.
    /// </summary>
    public static IServiceCollection AddAtalayaPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostgresOptions>(o =>
            o.ConnectionString = configuration.GetConnectionString("Postgres") ?? string.Empty);
        services.AddSingleton<IDeviceStateRepository, PostgresDeviceStateRepository>();
        services.AddSingleton<IAlertRepository, PostgresAlertRepository>();
        services.AddSingleton<ITelemetryArchive, PostgresTelemetryArchive>();
        return services;
    }
}
