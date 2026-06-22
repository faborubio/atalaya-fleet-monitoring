using Atalaya.Contracts;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Pruebas del motor de reglas por umbral (<see cref="AlertRules"/>, Fase 2). Fija los umbrales
/// y la severidad: el dashboard y los workers dependen de este contrato.
/// </summary>
public sealed class AlertRulesTests
{
    private static TelemetryEvent Event(
        double temp = 70, double fuel = 80, double speed = 50, string id = "evt-1") =>
        new(id, "dev-1", DateTimeOffset.UtcNow, 1, 19.4, -99.1, speed, 90, fuel, temp);

    [Fact]
    public void Telemetria_normal_no_dispara_alertas()
    {
        Assert.Empty(AlertRules.Evaluate(Event()));
    }

    [Theory]
    [InlineData(111, AlertSeverity.Critical)]
    [InlineData(100, AlertSeverity.Warning)]
    public void Temperatura_de_motor_dispara_segun_umbral(double temp, AlertSeverity expected)
    {
        var alert = Assert.Single(AlertRules.Evaluate(Event(temp: temp)));
        Assert.Equal("engine-temp-high", alert.Rule);
        Assert.Equal(expected, alert.Severity);
        Assert.Equal(temp, alert.Value);
    }

    [Theory]
    [InlineData(5, AlertSeverity.Critical)]
    [InlineData(15, AlertSeverity.Warning)]
    public void Combustible_bajo_dispara_segun_umbral(double fuel, AlertSeverity expected)
    {
        var alert = Assert.Single(AlertRules.Evaluate(Event(fuel: fuel)));
        Assert.Equal("fuel-low", alert.Rule);
        Assert.Equal(expected, alert.Severity);
    }

    [Theory]
    [InlineData(145, AlertSeverity.Critical)]
    [InlineData(125, AlertSeverity.Warning)]
    public void Exceso_de_velocidad_dispara_segun_umbral(double speed, AlertSeverity expected)
    {
        var alert = Assert.Single(AlertRules.Evaluate(Event(speed: speed)));
        Assert.Equal("overspeed", alert.Rule);
        Assert.Equal(expected, alert.Severity);
    }

    [Fact]
    public void Un_evento_puede_disparar_varias_reglas()
    {
        var alerts = AlertRules.Evaluate(Event(temp: 120, fuel: 5, speed: 150));
        Assert.Equal(3, alerts.Count);
        Assert.All(alerts, a => Assert.Equal(AlertSeverity.Critical, a.Severity));
        Assert.Equal(
            new[] { "engine-temp-high", "fuel-low", "overspeed" }.OrderBy(x => x),
            alerts.Select(a => a.Rule).OrderBy(x => x));
    }

    [Fact]
    public void AlertId_es_determinista_para_idempotencia()
    {
        var alert = Assert.Single(AlertRules.Evaluate(Event(temp: 120, id: "evt-42")));
        Assert.Equal("evt-42:engine-temp-high", alert.AlertId);
    }
}
