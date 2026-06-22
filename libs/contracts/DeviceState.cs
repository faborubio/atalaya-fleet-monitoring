namespace Atalaya.Contracts;

/// <summary>
/// Read model del camino caliente (SAD §6, ADR-005): última posición/estado conocido
/// por dispositivo. Una instancia por dispositivo, actualizada por el procesamiento y
/// servida al dashboard como snapshot inicial y como delta en vivo por SignalR.
/// </summary>
public sealed record DeviceState(
    string DeviceId,
    DateTimeOffset Ts,
    long Seq,
    double Lat,
    double Lng,
    double SpeedKmh,
    double HeadingDeg,
    double FuelPct,
    double EngineTempC)
{
    public static DeviceState FromEvent(TelemetryEvent e) => new(
        e.DeviceId, e.Ts, e.Seq, e.Lat, e.Lng,
        e.SpeedKmh, e.HeadingDeg, e.FuelPct, e.EngineTempC);
}
