namespace Atalaya.Worker;

/// <summary>
/// Configuración de Google Cloud Pub/Sub para el worker (ADR-013). Sección "Gcp". En dev se apunta
/// al <b>emulador</b> (<c>EmulatorHost</c>, p.ej. <c>localhost:8085</c>); en la nube real se deja
/// vacío y el cliente usa las credenciales del entorno (ADC). El topic lo comparte con la API; la
/// suscripción de pull es propia del worker.
/// </summary>
public sealed class GcpOptions
{
    public string ProjectId { get; set; } = "atalaya-local";
    public string TopicId { get; set; } = "atalaya-telemetry";
    public string SubscriptionId { get; set; } = "atalaya-telemetry-sub";
    /// <summary>Topic de mensajes envenenados (DLQ), espeja la DLQ de SQS (redrive maxReceiveCount=5).</summary>
    public string DeadLetterTopicId { get; set; } = "atalaya-telemetry-dlq";
    /// <summary>Intentos antes de enviar a la DLQ (paridad con SQS). Pub/Sub exige 5..100.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Host:puerto del emulador Pub/Sub. Vacío = Pub/Sub real (no auto-crea topología).</summary>
    public string EmulatorHost { get; set; } = string.Empty;

    public bool UsesEmulator => !string.IsNullOrWhiteSpace(EmulatorHost);
}
