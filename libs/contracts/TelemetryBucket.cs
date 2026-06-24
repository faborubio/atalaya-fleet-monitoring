namespace Atalaya.Contracts;

/// <summary>
/// Punto agregado del histórico (<b>downsampling</b>, AUD-028): un intervalo de tiempo con el
/// <b>promedio</b> de cada métrica y el conteo de eventos del intervalo. Permite servir rangos largos
/// con un número acotado de puntos (≈ ancho del gráfico) en vez de miles de filas crudas
/// (ADR-005/007: el camino frío no compite con el caliente). Las métricas se nombran igual que en
/// <see cref="TelemetryEvent"/> para que el frontend reuse el mismo gráfico/selector.
/// </summary>
public sealed record TelemetryBucket(
    DateTimeOffset Ts,
    int Count,
    double SpeedKmh,
    double FuelPct,
    double EngineTempC);
