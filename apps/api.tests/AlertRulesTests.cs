using Atalaya.Contracts;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Motor de reglas con histéresis (<see cref="AlertRules"/>, AUD-016/p1) + máquina de estados de
/// incidentes (<see cref="IncidentTransitions"/>). Fija umbrales, bandas de despeje y transiciones.
/// </summary>
public sealed class AlertRulesTests
{
    private static TelemetryEvent Event(
        double temp = 70, double fuel = 80, double speed = 50, string id = "evt-1") =>
        new(id, "dev-1", DateTimeOffset.UtcNow, 1, 19.4, -99.1, speed, 90, fuel, temp);

    [Fact]
    public void Telemetria_normal_no_emite_ningun_firing()
    {
        // Valores normales: solo señales Clear (que el store ignora si no hay incidente abierto).
        Assert.DoesNotContain(AlertRules.Read(Event()), r => r.Signal == RuleSignal.Firing);
    }

    [Theory]
    [InlineData(111, AlertSeverity.Critical)]
    [InlineData(100, AlertSeverity.Warning)]
    public void Temperatura_alta_emite_firing(double temp, AlertSeverity expected)
    {
        var r = Assert.Single(AlertRules.Read(Event(temp: temp)), x => x.Rule == "engine-temp-high");
        Assert.Equal(RuleSignal.Firing, r.Signal);
        Assert.Equal(expected, r.Severity);
    }

    [Fact]
    public void En_la_banda_de_histeresis_no_emite_senal()
    {
        // 92 °C: por debajo del aviso (95) pero por encima del despeje (90) → zona neutra.
        Assert.DoesNotContain(AlertRules.Read(Event(temp: 92)), r => r.Rule == "engine-temp-high");
    }

    [Fact]
    public void Por_debajo_del_despeje_emite_clear()
    {
        var r = Assert.Single(AlertRules.Read(Event(temp: 80)), x => x.Rule == "engine-temp-high");
        Assert.Equal(RuleSignal.Clear, r.Signal);
    }

    private static RuleReading Firing(AlertSeverity sev, double v = 100, string ts = "2026-06-22T10:00:00Z") =>
        new("dev-1", "engine-temp-high", RuleSignal.Firing, sev, v, DateTimeOffset.Parse(ts), "m");

    private static RuleReading Clear(string ts = "2026-06-22T10:01:00Z") =>
        new("dev-1", "engine-temp-high", RuleSignal.Clear, AlertSeverity.Warning, 80, DateTimeOffset.Parse(ts), "m");

    [Fact]
    public void Abre_un_incidente_en_el_primer_firing()
    {
        var (next, transition) = IncidentTransitions.Decide(null, Firing(AlertSeverity.Warning));
        Assert.True(transition);
        Assert.Equal(IncidentStatus.Open, next!.Status);
    }

    [Fact]
    public void Firing_repetido_sin_escalar_no_es_transicion()
    {
        var (open, _) = IncidentTransitions.Decide(null, Firing(AlertSeverity.Warning));
        var (_, transition) = IncidentTransitions.Decide(open, Firing(AlertSeverity.Warning, ts: "2026-06-22T10:00:05Z"));
        Assert.False(transition); // ya abierto, misma severidad → solo actualiza valor
    }

    [Fact]
    public void Escalar_de_aviso_a_critico_es_transicion()
    {
        var (open, _) = IncidentTransitions.Decide(null, Firing(AlertSeverity.Warning));
        var (next, transition) = IncidentTransitions.Decide(open, Firing(AlertSeverity.Critical, ts: "2026-06-22T10:00:10Z"));
        Assert.True(transition);
        Assert.Equal(AlertSeverity.Critical, next!.Severity);
    }

    [Fact]
    public void Clear_resuelve_un_incidente_abierto()
    {
        var (open, _) = IncidentTransitions.Decide(null, Firing(AlertSeverity.Critical));
        var (next, transition) = IncidentTransitions.Decide(open, Clear());
        Assert.True(transition);
        Assert.Equal(IncidentStatus.Resolved, next!.Status);
    }

    [Fact]
    public void Clear_sin_incidente_abierto_no_hace_nada()
    {
        var (_, transition) = IncidentTransitions.Decide(null, Clear());
        Assert.False(transition);
    }

    // Cooldown anti-flapping (AUDIT §8.17): tras resolver, un firing dentro de la ventana no reabre.
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    [Fact]
    public void Cooldown_suprime_la_reapertura_dentro_de_la_ventana()
    {
        var (open, _) = IncidentTransitions.Decide(null, Firing(AlertSeverity.Critical, ts: "2026-06-22T10:00:00Z"));
        var (resolved, _) = IncidentTransitions.Decide(open, Clear(ts: "2026-06-22T10:01:00Z"));

        // Firing 2 min después de resolver, con cooldown de 5 min → no reabre (telemetría ruidosa).
        var (next, transition) = IncidentTransitions.Decide(
            resolved, Firing(AlertSeverity.Critical, ts: "2026-06-22T10:03:00Z"), Cooldown);

        Assert.False(transition);
        Assert.Equal(IncidentStatus.Resolved, next!.Status);
    }

    [Fact]
    public void Tras_el_cooldown_un_firing_reabre_el_incidente()
    {
        var (open, _) = IncidentTransitions.Decide(null, Firing(AlertSeverity.Critical, ts: "2026-06-22T10:00:00Z"));
        var (resolved, _) = IncidentTransitions.Decide(open, Clear(ts: "2026-06-22T10:01:00Z"));

        // Firing 6 min después de resolver (fuera del cooldown de 5 min) → reabre.
        var (next, transition) = IncidentTransitions.Decide(
            resolved, Firing(AlertSeverity.Critical, ts: "2026-06-22T10:07:00Z"), Cooldown);

        Assert.True(transition);
        Assert.Equal(IncidentStatus.Open, next!.Status);
    }

    [Fact]
    public void Sin_cooldown_un_firing_reabre_de_inmediato()
    {
        var (open, _) = IncidentTransitions.Decide(null, Firing(AlertSeverity.Critical, ts: "2026-06-22T10:00:00Z"));
        var (resolved, _) = IncidentTransitions.Decide(open, Clear(ts: "2026-06-22T10:01:00Z"));

        // Sin cooldown (default): la reapertura inmediata es una transición (comportamiento previo).
        var (next, transition) = IncidentTransitions.Decide(
            resolved, Firing(AlertSeverity.Critical, ts: "2026-06-22T10:01:30Z"));

        Assert.True(transition);
        Assert.Equal(IncidentStatus.Open, next!.Status);
    }
}
