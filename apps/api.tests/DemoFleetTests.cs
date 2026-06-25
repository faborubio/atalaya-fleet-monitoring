using Atalaya.Api.Processing;
using Atalaya.Api.Services;
using Atalaya.Contracts;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Generador de telemetría de demo (<see cref="DemoFleet"/>, ADR-014). Fija: un evento por
/// dispositivo por tick, secuencia monótona, anomalías que disparan alertas, y que sin anomalías
/// programadas todo se mantiene en rango sano.
/// </summary>
public sealed class DemoFleetTests
{
    private static DemoFleet Fleet(int devices = 6, int alertEvery = 2) =>
        new(new DemoOptions { Devices = devices, AlertEveryNTicks = alertEvery, Seed = 42 });

    [Fact]
    public void Step_genera_un_evento_por_dispositivo()
    {
        var fleet = Fleet(devices: 6);
        var batch = fleet.Step();
        Assert.Equal(6, batch.Count);
        Assert.Equal(6, batch.Select(e => e.DeviceId).Distinct().Count());
    }

    [Fact]
    public void Seq_es_monotona_creciente_entre_ticks()
    {
        var fleet = Fleet(devices: 3);
        var first = fleet.Step().Select(e => e.Seq).ToList();
        var second = fleet.Step().Select(e => e.Seq).ToList();
        Assert.True(second.Min() > first.Max());
    }

    [Fact]
    public void Inyecta_anomalias_que_disparan_alertas()
    {
        var fleet = Fleet(devices: 6, alertEvery: 2);
        var anyFiring = false;
        for (var i = 0; i < 30 && !anyFiring; i++)
            anyFiring = fleet.Step().SelectMany(AlertRules.Read).Any(r => r.Signal == RuleSignal.Firing);
        Assert.True(anyFiring, "el generador debería producir al menos una alerta (Firing) en 30 ticks");
    }

    [Fact]
    public void Sin_anomalias_la_telemetria_se_mantiene_sana()
    {
        // AlertEveryNTicks=0 → nunca inyecta anomalías; ninguna señal Firing en rango normal.
        var fleet = new DemoFleet(new DemoOptions { Devices = 10, AlertEveryNTicks = 0, Seed = 7 });
        for (var i = 0; i < 20; i++)
            Assert.DoesNotContain(fleet.Step().SelectMany(AlertRules.Read), r => r.Signal == RuleSignal.Firing);
    }
}
