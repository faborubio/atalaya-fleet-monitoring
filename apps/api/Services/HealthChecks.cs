using Amazon.SimpleNotificationService;
using Atalaya.Persistence;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace Atalaya.Api.Services;

/// <summary>
/// Readiness gateada por dependencias (SAD Fase 3, AUD-015 E): la API solo está "ready" si puede
/// hablar con Postgres (read models), Redis (puente de push) y SNS (ingesta). Liveness ≠ readiness:
/// un proceso vivo pero sin BD no debe recibir tráfico en un rolling deploy.
/// </summary>
public sealed class PostgresHealthCheck(IOptions<PostgresOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(options.Value.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1;", conn);
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Postgres inaccesible", ex);
        }
    }
}

public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis inaccesible", ex);
        }
    }
}

public sealed class SnsHealthCheck(IAmazonSimpleNotificationService sns) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await sns.ListTopicsAsync(new Amazon.SimpleNotificationService.Model.ListTopicsRequest(), ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SNS inaccesible", ex);
        }
    }
}

/// <summary>Readiness del broker en modo Gcp (ADR-013): el topic de ingesta debe ser alcanzable.</summary>
public sealed class PubSubHealthCheck(PublisherServiceApiClient publisher, GcpOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await publisher.GetTopicAsync(TopicName.FromProjectTopic(options.ProjectId, options.TopicId), ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Pub/Sub inaccesible", ex);
        }
    }
}
