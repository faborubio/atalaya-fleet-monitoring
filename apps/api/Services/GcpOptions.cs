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

    /// <summary>Suscripción sobre el topic DLQ, leída por el replay de la DLQ (ADR-006). La crea el
    /// worker contra el emulador y Terraform en la nube.</summary>
    public string DeadLetterSubscriptionId { get; set; } = "atalaya-telemetry-dlq-sub";

    /// <summary>Host:puerto del emulador Pub/Sub. Vacío = Pub/Sub real (no auto-crea topología).</summary>
    public string EmulatorHost { get; set; } = string.Empty;

    // Analítica con BigQuery sobre el data lake (G4, ADR-013). El endpoint /api/analytics solo se
    // registra si hay dataset configurado (no hay emulador decente de BigQuery → se valida contra el
    // proyecto real; ausente en base/tests). Equivale a Athena sobre S3 en la era AWS.
    /// <summary>Dataset de BigQuery (p.ej. <c>atalaya_analytics</c>). Vacío = analítica deshabilitada.</summary>
    public string DatasetId { get; set; } = string.Empty;
    /// <summary>External table sobre el lake GCS (NDJSON). La crea <c>scripts/bigquery-setup.mjs</c>.</summary>
    public string AnalyticsTable { get; set; } = "telemetry_raw";

    /// <summary>
    /// Tope de bytes facturados por consulta (cost guard, revisión crítica G4): la external table no
    /// tiene poda de particiones (layout yyyy/MM/dd, no hive) → cada query escanea todo el lake, y
    /// BigQuery es pay-per-byte. Si una consulta excediera este tope, BigQuery la <b>rechaza</b> en
    /// vez de facturarla. Default 1 GB (holgado para dev; súbelo conscientemente). 0 = sin tope.
    /// </summary>
    public long AnalyticsMaxBytesBilled { get; set; } = 1_000_000_000;

    /// <summary>True si hay dataset configurado → se exponen las consultas analíticas.</summary>
    public bool AnalyticsEnabled => !string.IsNullOrWhiteSpace(DatasetId);

    // Publicación desacoplada (AUD-009): cola en memoria + lotes en background, igual que SNS.
    /// <summary>Capacidad del canal de ingesta (eventos en vuelo antes de aplicar backpressure).</summary>
    public int PublisherQueueCapacity { get; set; } = 200_000;
    /// <summary>Máximo de eventos por mensaje Pub/Sub (controla el tamaño, &lt;10 MB).</summary>
    public int MessageMaxEvents { get; set; } = 100;
    /// <summary>Ventana de coalescencia del publicador en ms (acota la latencia añadida).</summary>
    public int FlushMilliseconds { get; set; } = 25;

    public bool UsesEmulator => !string.IsNullOrWhiteSpace(EmulatorHost);
}
