using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Atalaya.Realtime;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra la conexión a Redis (multiplexor compartido) y el deduplicador de eventos.
    /// Toma ConnectionStrings:Redis (o "localhost:6379" por defecto).
    /// </summary>
    public static IServiceCollection AddAtalayaRedis(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.Configure<RedisOptions>(o => o.Configuration = connStr);
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(connStr));
        services.AddSingleton<IEventDeduplicator, RedisEventDeduplicator>();
        return services;
    }
}
