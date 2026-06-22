using Atalaya.Contracts;
using Atalaya.Persistence;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Clave idempotente del data lake S3 (<see cref="RawEventKey"/>, AUD-015 p3): el mismo lote
/// produce siempre la misma clave (reentrega = sobrescritura, no duplicado); contenido distinto,
/// clave distinta; la partición sale del evento más antiguo.
/// </summary>
public sealed class RawEventKeyTests
{
    private static TelemetryEvent Event(string id, DateTimeOffset ts) =>
        new(id, "dev-1", ts, 1, 19.4, -99.1, 50, 90, 80, 70);

    [Fact]
    public void Mismo_lote_misma_clave()
    {
        var ts = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var a = new[] { Event("e1", ts), Event("e2", ts.AddSeconds(1)) };
        var b = new[] { Event("e1", ts), Event("e2", ts.AddSeconds(1)) };

        Assert.Equal(RawEventKey.Build(a).Key, RawEventKey.Build(b).Key);
    }

    [Fact]
    public void Contenido_distinto_clave_distinta()
    {
        var ts = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);
        var a = new[] { Event("e1", ts) };
        var b = new[] { Event("e2", ts) };

        Assert.NotEqual(RawEventKey.Build(a).Key, RawEventKey.Build(b).Key);
    }

    [Fact]
    public void La_particion_usa_la_fecha_del_evento_mas_antiguo()
    {
        var older = new DateTimeOffset(2026, 6, 21, 23, 30, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 6, 22, 0, 30, 0, TimeSpan.Zero);
        var (key, _) = RawEventKey.Build(new[] { Event("n", newer), Event("o", older) });

        Assert.StartsWith("raw/2026/06/21/", key);
        Assert.EndsWith(".json", key);
    }
}
