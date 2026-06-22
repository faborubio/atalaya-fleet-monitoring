namespace Atalaya.Contracts;

/// <summary>
/// Evento de telemetría tal como lo emiten los dispositivos / el simulador (SAD §6).
/// <para><see cref="EventId"/> habilita la deduplicación idempotente (ADR-006).</para>
/// <para><see cref="Seq"/> es la secuencia por dispositivo, base para detectar y
/// rellenar gaps en reconexión.</para>
/// </summary>
public sealed record TelemetryEvent(
    string EventId,
    string DeviceId,
    DateTimeOffset Ts,
    long Seq,
    double Lat,
    double Lng,
    double SpeedKmh,
    double HeadingDeg,
    double FuelPct,
    double EngineTempC);
