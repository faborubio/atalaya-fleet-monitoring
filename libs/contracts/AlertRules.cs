namespace Atalaya.Contracts;

/// <summary>
/// Motor de reglas por umbral (SAD §1.3, Fase 2). Puro y sin dependencias: dado un evento
/// de telemetría, emite las alertas que dispara. Vive en contracts para compartirse entre el
/// worker (camino Aws) y el procesador en memoria (dev/tests) sin duplicar lógica.
/// <para>Cada regla emite a lo sumo una alerta por evento, en su nivel más alto (p. ej. una
/// temperatura crítica no dispara además la de aviso). El <c>AlertId</c> es determinista para
/// idempotencia (ADR-006).</para>
/// </summary>
public static class AlertRules
{
    // Umbrales (SAD §6). Constantes para que las pruebas y el dashboard hablen del mismo número.
    public const double EngineTempCriticalC = 110;
    public const double EngineTempWarningC = 95;
    public const double FuelCriticalPct = 10;
    public const double FuelWarningPct = 20;
    public const double SpeedCriticalKmh = 140;
    public const double SpeedWarningKmh = 120;

    /// <summary>Evalúa un evento y devuelve las alertas disparadas (puede ser vacío).</summary>
    public static IReadOnlyList<Alert> Evaluate(TelemetryEvent e)
    {
        var alerts = new List<Alert>(3);

        if (e.EngineTempC >= EngineTempCriticalC)
            alerts.Add(Make(e, "engine-temp-high", AlertSeverity.Critical, e.EngineTempC,
                $"Temperatura de motor crítica: {e.EngineTempC:0.#} °C"));
        else if (e.EngineTempC >= EngineTempWarningC)
            alerts.Add(Make(e, "engine-temp-high", AlertSeverity.Warning, e.EngineTempC,
                $"Temperatura de motor alta: {e.EngineTempC:0.#} °C"));

        if (e.FuelPct <= FuelCriticalPct)
            alerts.Add(Make(e, "fuel-low", AlertSeverity.Critical, e.FuelPct,
                $"Combustible crítico: {e.FuelPct:0.#} %"));
        else if (e.FuelPct <= FuelWarningPct)
            alerts.Add(Make(e, "fuel-low", AlertSeverity.Warning, e.FuelPct,
                $"Combustible bajo: {e.FuelPct:0.#} %"));

        if (e.SpeedKmh >= SpeedCriticalKmh)
            alerts.Add(Make(e, "overspeed", AlertSeverity.Critical, e.SpeedKmh,
                $"Exceso de velocidad grave: {e.SpeedKmh:0.#} km/h"));
        else if (e.SpeedKmh >= SpeedWarningKmh)
            alerts.Add(Make(e, "overspeed", AlertSeverity.Warning, e.SpeedKmh,
                $"Exceso de velocidad: {e.SpeedKmh:0.#} km/h"));

        return alerts;
    }

    private static Alert Make(TelemetryEvent e, string rule, AlertSeverity sev, double value, string message)
        => new($"{e.EventId}:{rule}", e.DeviceId, rule, sev, value, e.Ts, message);
}
