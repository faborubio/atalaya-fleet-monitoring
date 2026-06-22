using Atalaya.Persistence;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Convención de nombres de partición (<see cref="PartitionName"/>, AUD-015 p2): ida y vuelta
/// fecha↔nombre y rechazo de nombres que no encajan (la retención no debe tocar tablas ajenas).
/// </summary>
public sealed class PartitionNameTests
{
    [Fact]
    public void ForDate_y_DateOf_son_inversas()
    {
        var date = new DateOnly(2026, 6, 22);
        var name = PartitionName.ForDate(date);

        Assert.Equal("telemetry_p20260622", name);
        Assert.Equal(date, PartitionName.DateOf(name));
    }

    [Theory]
    [InlineData("device_state")]
    [InlineData("alerts")]
    [InlineData("telemetry")]
    [InlineData("telemetry_pZZZZ")]
    [InlineData("telemetry_p2026")]
    public void DateOf_rechaza_lo_que_no_es_particion_de_telemetry(string name)
    {
        Assert.Null(PartitionName.DateOf(name));
    }

    [Fact]
    public void DateOf_selecciona_solo_las_anteriores_al_cutoff()
    {
        var cutoff = new DateOnly(2026, 6, 20);
        var partitions = new[]
        {
            "telemetry_p20260618", // vieja → se dropea
            "telemetry_p20260619", // vieja → se dropea
            "telemetry_p20260620", // == cutoff → se conserva
            "telemetry_p20260621", // nueva → se conserva
            "device_state",        // ajena → se ignora
        };

        var toDrop = partitions
            .Where(p => PartitionName.DateOf(p) is { } d && d < cutoff)
            .ToArray();

        Assert.Equal(new[] { "telemetry_p20260618", "telemetry_p20260619" }, toDrop);
    }
}
