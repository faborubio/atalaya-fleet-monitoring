namespace Atalaya.Api.Services;

/// <summary>
/// Configuración de Google Cloud Pub/Sub (ADR-013). Sección "Gcp". En dev se apunta al
/// <b>emulador</b> (variable <c>EmulatorHost</c>, p.ej. <c>localhost:8085</c>) para no gastar; en la
/// nube real se deja vacío y el cliente usa las credenciales del entorno (ADC).
/// </summary>
public sealed class GcpOptions
{
    public string ProjectId { get; set; } = "atalaya-local";
    public string TopicId { get; set; } = "atalaya-telemetry";

    /// <summary>Host:puerto del emulador Pub/Sub. Vacío = Pub/Sub real (no auto-crea topología).</summary>
    public string EmulatorHost { get; set; } = string.Empty;

    // Publicación desacoplada (AUD-009): cola en memoria + lotes en background, igual que SNS.
    /// <summary>Capacidad del canal de ingesta (eventos en vuelo antes de aplicar backpressure).</summary>
    public int PublisherQueueCapacity { get; set; } = 200_000;
    /// <summary>Máximo de eventos por mensaje Pub/Sub (controla el tamaño, &lt;10 MB).</summary>
    public int MessageMaxEvents { get; set; } = 100;
    /// <summary>Ventana de coalescencia del publicador en ms (acota la latencia añadida).</summary>
    public int FlushMilliseconds { get; set; } = 25;

    public bool UsesEmulator => !string.IsNullOrWhiteSpace(EmulatorHost);
}
