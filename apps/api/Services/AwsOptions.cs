namespace Atalaya.Api.Services;

/// <summary>Configuración de acceso a AWS (LocalStack en dev). Sección "Aws".</summary>
public sealed class AwsOptions
{
    public string ServiceUrl { get; set; } = "http://localhost:4566";
    public string Region { get; set; } = "us-east-1";
    public string TopicName { get; set; } = "atalaya-telemetry";

    // Publicación desacoplada a SNS (AUD-009): cola en memoria + lotes en background.
    /// <summary>Capacidad del canal de ingesta (eventos en vuelo antes de aplicar backpressure).</summary>
    public int PublisherQueueCapacity { get; set; } = 200_000;
    /// <summary>Máximo de eventos por mensaje SNS (controla el tamaño, &lt;256 KB).</summary>
    public int MessageMaxEvents { get; set; } = 100;
    /// <summary>Ventana de coalescencia del publicador en ms (acota la latencia añadida).</summary>
    public int FlushMilliseconds { get; set; } = 25;
}
