using Atalaya.Contracts;
using Atalaya.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Integración del store de incidentes contra Postgres real (Testcontainers): abrir, escalar y
/// resolver con la lógica SQL (<c>unnest</c>/<c>ON CONFLICT</c> + caché de abiertos), que el modo
/// InMemory no ejercita.
/// </summary>
[Collection("postgres")]
public sealed class PostgresIncidentTests(PostgresContainerFixture pg)
{
    private static PostgresAlertIncidentStore Store(string cs) =>
        new(Options.Create(new PostgresOptions { ConnectionString = cs }));

    private static RuleReading Firing(string device, AlertSeverity sev, double v, DateTimeOffset ts) =>
        new(device, "engine-temp-high", RuleSignal.Firing, sev, v, ts, "m");

    private static RuleReading Clear(string device, DateTimeOffset ts) =>
        new(device, "engine-temp-high", RuleSignal.Clear, AlertSeverity.Warning, 80, ts, "m");

    [SkippableFact]
    public async Task Abre_escala_y_resuelve_un_incidente()
    {
        Skip.IfNot(pg.Available, "Docker no disponible");
        var store = Store(pg.ConnectionString);
        await store.EnsureSchemaAsync();
        var dev = $"dev-inc-{Guid.NewGuid():N}";
        var t = DateTimeOffset.UtcNow;

        // Abrir (aviso).
        var open = await store.ApplyAsync(new[] { Firing(dev, AlertSeverity.Warning, 100, t) });
        var o = Assert.Single(open);
        Assert.Equal(IncidentStatus.Open, o.Status);
        Assert.Equal(AlertSeverity.Warning, o.Severity);

        // Firing repetido misma severidad: no es transición.
        var repeat = await store.ApplyAsync(new[] { Firing(dev, AlertSeverity.Warning, 101, t.AddSeconds(1)) });
        Assert.Empty(repeat);

        // Escalar a crítico: transición.
        var esc = await store.ApplyAsync(new[] { Firing(dev, AlertSeverity.Critical, 115, t.AddSeconds(2)) });
        Assert.Equal(AlertSeverity.Critical, Assert.Single(esc).Severity);

        // Resolver al normalizar.
        var res = await store.ApplyAsync(new[] { Clear(dev, t.AddSeconds(3)) });
        Assert.Equal(IncidentStatus.Resolved, Assert.Single(res).Status);

        // GetActive: el incidente sigue (resuelto) con su última severidad.
        var active = await store.GetActiveAsync();
        var inc = Assert.Single(active, i => i.DeviceId == dev);
        Assert.Equal(IncidentStatus.Resolved, inc.Status);
    }

    [SkippableFact]
    public async Task Clear_sin_incidente_abierto_no_crea_nada()
    {
        Skip.IfNot(pg.Available, "Docker no disponible");
        var store = Store(pg.ConnectionString);
        await store.EnsureSchemaAsync();
        var dev = $"dev-noop-{Guid.NewGuid():N}";

        var t = await store.ApplyAsync(new[] { Clear(dev, DateTimeOffset.UtcNow) });
        Assert.Empty(t);
        Assert.DoesNotContain(await store.GetActiveAsync(), i => i.DeviceId == dev);
    }
}
