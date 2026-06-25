namespace Atalaya.Api.Services;

/// <summary>
/// Configuración del generador de telemetría de demo de portafolio (ADR-014, ver DEMO.md). Sección
/// "Demo". <see cref="Enabled"/> default <c>false</c> → ausente en dev/tests/prod normal; solo se
/// activa en el servicio de demo InMemory always-on (Cloud Run scale-to-zero). El generador inyecta
/// datos por el mismo <c>ITelemetryPublisher</c> que <c>/ingest</c>, así que reusa todo el pipeline.
/// </summary>
public sealed class DemoOptions
{
    /// <summary>Activa el generador. Solo tiene sentido con <c>Telemetry:Transport=InMemory</c>.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Número de dispositivos sintéticos en la flota de demo.</summary>
    public int Devices { get; set; } = 40;

    /// <summary>Período entre ticks en ms (cada tick emite un evento por dispositivo).</summary>
    public int IntervalMs { get; set; } = 1000;

    // Centro del cluster (default: CDMX) para que la flota se vea agrupada y reconocible en el mapa.
    public double Lat { get; set; } = 19.4326;
    public double Lng { get; set; } = -99.1332;

    /// <summary>Dispersión inicial (grados) de la flota alrededor del centro.</summary>
    public double SpreadDeg { get; set; } = 0.06;

    /// <summary>Cada cuántos ticks se inyecta una anomalía (que dispara una alerta visible). 0 = nunca.</summary>
    public int AlertEveryNTicks { get; set; } = 6;

    /// <summary>Semilla del RNG. 0 = aleatoria (producción); &gt;0 = determinista (tests).</summary>
    public int Seed { get; set; } = 0;
}
