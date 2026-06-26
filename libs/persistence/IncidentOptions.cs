namespace Atalaya.Persistence;

/// <summary>
/// Configuración de la máquina de incidentes (sección "Alerts"). Hoy solo el <b>cooldown</b>
/// anti-<i>flapping</i> (AUDIT §8.17): tras resolverse, una regla no reabre incidente en el mismo
/// dispositivo durante esta ventana.
/// </summary>
public sealed class IncidentOptions
{
    /// <summary>Segundos de enfriamiento tras resolver antes de poder reabrir. 0 = sin cooldown.</summary>
    public int CooldownSeconds { get; set; } = 300;

    /// <summary>Cooldown como <see cref="TimeSpan"/> (derivado de <see cref="CooldownSeconds"/>).</summary>
    public TimeSpan Cooldown => TimeSpan.FromSeconds(Math.Max(0, CooldownSeconds));
}
