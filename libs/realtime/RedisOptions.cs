namespace Atalaya.Realtime;

/// <summary>Configuración de Redis. ConnectionStrings:Redis + ajustes de dedup.</summary>
public sealed class RedisOptions
{
    public string Configuration { get; set; } = "localhost:6379";
    /// <summary>TTL de las claves de deduplicación (ventana de idempotencia).</summary>
    public int DedupTtlSeconds { get; set; } = 3600;
}
