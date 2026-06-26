namespace Atalaya.Contracts;

/// <summary>
/// Máquina de estados de un incidente (AUD-016/p1), pura y compartida por los stores (Postgres e
/// in-memory). Dada la señal de una regla y el incidente actual, decide el siguiente estado y si
/// hubo <b>transición</b> (lo único que se notifica): abrir, escalar severidad o resolver.
/// </summary>
public static class IncidentTransitions
{
    /// <summary>
    /// Cooldown por defecto (AUDIT §8.17): tras resolverse, un incidente no se reabre por la misma
    /// regla y dispositivo durante esta ventana, ignorando telemetría ruidosa (sensor con <i>flapping</i>).
    /// </summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Decide el incidente resultante para una lectura. <c>Next</c> es el estado a persistir
    /// (o <c>null</c> si no hay nada que guardar); <c>Transition</c> indica si debe notificarse.
    /// <para>
    /// <paramref name="cooldown"/> (AUDIT §8.17, anti-<i>flapping</i>): si es &gt; 0 y la regla vuelve
    /// a dispararse antes de que pasen <c>cooldown</c> desde que el incidente se resolvió, la reapertura
    /// se <b>suprime</b> (no transiciona, no notifica). El tiempo se mide con el <c>Ts</c> del evento
    /// (no reloj de pared), coherente con el orden por <c>seq</c>. <c>default</c> (cero) = sin cooldown.
    /// </para>
    /// </summary>
    public static (AlertIncident? Next, bool Transition) Decide(
        AlertIncident? current, RuleReading r, TimeSpan cooldown = default)
    {
        var id = AlertIncident.Id(r.DeviceId, r.Rule);

        if (r.Signal == RuleSignal.Firing)
        {
            // Cooldown anti-flapping: resuelto hace poco → ignora el firing ruidoso, sin reabrir.
            if (current is { Status: IncidentStatus.Resolved }
                && cooldown > TimeSpan.Zero
                && r.Ts - current.UpdatedAt < cooldown)
                return (current, false);

            // Abrir: no existe o estaba resuelto (y fuera del cooldown).
            if (current is null || current.Status == IncidentStatus.Resolved)
            {
                var opened = new AlertIncident(
                    id, r.DeviceId, r.Rule, r.Severity, IncidentStatus.Open,
                    r.Value, r.Ts, r.Ts, r.Message);
                return (opened, true);
            }

            // Escalar: ya abierto, pero la severidad sube (p. ej. aviso → crítico).
            if (r.Severity > current.Severity)
                return (current with
                {
                    Severity = r.Severity, Value = r.Value, UpdatedAt = r.Ts, Message = r.Message
                }, true);

            // Sigue abierto sin cambio relevante: actualiza el último valor, no notifica.
            return (current with { Value = r.Value, UpdatedAt = r.Ts }, false);
        }

        // Clear: resolver solo si estaba abierto.
        if (current is { Status: IncidentStatus.Open })
            return (current with
            {
                Status = IncidentStatus.Resolved, Value = r.Value, UpdatedAt = r.Ts, Message = r.Message
            }, true);

        // Clear sin incidente abierto: nada que hacer.
        return (current, false);
    }

    /// <summary>
    /// Reduce las lecturas de un lote a una por <c>(deviceId, rule)</c>: la más reciente. Evita
    /// abrir y cerrar el mismo incidente varias veces dentro de un lote.
    /// </summary>
    public static IEnumerable<RuleReading> Latest(IEnumerable<RuleReading> readings) =>
        readings
            .GroupBy(r => AlertIncident.Id(r.DeviceId, r.Rule))
            .Select(g => g.MaxBy(r => r.Ts)!);
}
