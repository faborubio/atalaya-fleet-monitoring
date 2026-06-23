namespace Atalaya.Contracts;

/// <summary>Señal de una regla sobre un evento: o entra en alerta, o vuelve a la normalidad.</summary>
public enum RuleSignal
{
    Firing,
    Clear,
}

/// <summary>
/// Lectura de una regla sobre un evento (AUD-016/p1): no es una alerta, es la señal que el
/// motor de incidentes usa para decidir transiciones. <see cref="RuleSignal.Firing"/> trae la
/// severidad; <see cref="RuleSignal.Clear"/> indica normalización.
/// </summary>
public sealed record RuleReading(
    string DeviceId,
    string Rule,
    RuleSignal Signal,
    AlertSeverity Severity,
    double Value,
    DateTimeOffset Ts,
    string Message);

/// <summary>
/// Motor de reglas por umbral con <b>histéresis</b> (SAD §1.3, AUD-016/p1). Cada regla tiene una
/// banda de disparo (warning/critical) y una banda de despeje **separada** para evitar
/// <i>flapping</i> (abrir a 95 °C, cerrar a 90). Entre ambas, la regla no emite señal (mantiene el
/// estado del incidente). Puro y sin estado: la decisión de abrir/cerrar la toma el incident store.
/// </summary>
public static class AlertRules
{
    // Umbrales (SAD §6). Disparo y despeje con margen de histéresis.
    public const double EngineTempCriticalC = 110;
    public const double EngineTempWarningC = 95;
    public const double EngineTempClearC = 90;

    public const double FuelCriticalPct = 10;
    public const double FuelWarningPct = 20;
    public const double FuelClearPct = 25;

    public const double SpeedCriticalKmh = 140;
    public const double SpeedWarningKmh = 120;
    public const double SpeedClearKmh = 115;

    /// <summary>Señales que dispara un evento (vacío si todo está en zona neutra).</summary>
    public static IReadOnlyList<RuleReading> Read(TelemetryEvent e)
    {
        var readings = new List<RuleReading>(3);

        AddHigh(readings, e, "engine-temp-high", e.EngineTempC,
            EngineTempWarningC, EngineTempCriticalC, EngineTempClearC, "Temperatura de motor", "°C");
        AddLow(readings, e, "fuel-low", e.FuelPct,
            FuelWarningPct, FuelCriticalPct, FuelClearPct, "Combustible", "%");
        AddHigh(readings, e, "overspeed", e.SpeedKmh,
            SpeedWarningKmh, SpeedCriticalKmh, SpeedClearKmh, "Velocidad", "km/h");

        return readings;
    }

    // Reglas "cuanto más alto peor" (temperatura, velocidad).
    private static void AddHigh(
        List<RuleReading> acc, TelemetryEvent e, string rule, double value,
        double warn, double crit, double clear, string label, string unit)
    {
        if (value >= crit)
            acc.Add(Firing(e, rule, AlertSeverity.Critical, value, $"{label} crítica: {value:0.#} {unit}"));
        else if (value >= warn)
            acc.Add(Firing(e, rule, AlertSeverity.Warning, value, $"{label} alta: {value:0.#} {unit}"));
        else if (value < clear)
            acc.Add(Clear(e, rule, value, $"{label} normalizada: {value:0.#} {unit}"));
    }

    // Reglas "cuanto más bajo peor" (combustible).
    private static void AddLow(
        List<RuleReading> acc, TelemetryEvent e, string rule, double value,
        double warn, double crit, double clear, string label, string unit)
    {
        if (value <= crit)
            acc.Add(Firing(e, rule, AlertSeverity.Critical, value, $"{label} crítico: {value:0.#} {unit}"));
        else if (value <= warn)
            acc.Add(Firing(e, rule, AlertSeverity.Warning, value, $"{label} bajo: {value:0.#} {unit}"));
        else if (value > clear)
            acc.Add(Clear(e, rule, value, $"{label} normalizado: {value:0.#} {unit}"));
    }

    private static RuleReading Firing(TelemetryEvent e, string rule, AlertSeverity sev, double value, string msg)
        => new(e.DeviceId, rule, RuleSignal.Firing, sev, value, e.Ts, msg);

    private static RuleReading Clear(TelemetryEvent e, string rule, double value, string msg)
        => new(e.DeviceId, rule, RuleSignal.Clear, AlertSeverity.Warning, value, e.Ts, msg);
}
