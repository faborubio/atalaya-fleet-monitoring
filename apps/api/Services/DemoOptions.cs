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

    /// <summary>
    /// Multiplicador de la velocidad de avance en el mapa. 1.0 = tiempo real (un punto a 50 km/h se
    /// mueve a 50 km/h); &gt;1 acelera para una demo más vivaz sin teletransportar.
    /// </summary>
    public double SpeedFactor { get; set; } = 1.0;

    // Centro del cluster (default: Santiago de Chile) para que la flota se vea agrupada y reconocible
    // en el mapa. El centro reticular de Santiago se presta al movimiento en cuadrícula (grid-snap).
    public double Lat { get; set; } = -33.4489;
    public double Lng { get; set; } = -70.6693;

    /// <summary>Dispersión inicial (grados) de la flota alrededor del centro.</summary>
    public double SpreadDeg { get; set; } = 0.06;

    /// <summary>Cada cuántos ticks se inyecta una anomalía (que dispara una alerta visible). 0 = nunca.</summary>
    public int AlertEveryNTicks { get; set; } = 6;

    /// <summary>Semilla del RNG. 0 = aleatoria (producción); &gt;0 = determinista (tests).</summary>
    public int Seed { get; set; } = 0;
}
