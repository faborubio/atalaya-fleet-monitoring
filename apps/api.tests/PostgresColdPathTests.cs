using Atalaya.Contracts;
using Atalaya.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Integración del camino frío contra Postgres real (Testcontainers): particionado, consulta por
/// rango y retención por <c>DROP PARTITION</c> — lo que el modo InMemory no ejercita.
/// </summary>
[Collection("postgres")]
public sealed class PostgresColdPathTests(PostgresContainerFixture pg)
{
    private PostgresTelemetryArchive Archive() =>
        new(Options.Create(new PostgresOptions { ConnectionString = pg.ConnectionString }));

    private static TelemetryEvent Event(string id, string device, DateTimeOffset ts) =>
        new(id, device, ts, 1, 19.4, -99.1, 50, 90, 80, 70);

    [SkippableFact]
    public async Task Particiona_por_dia_consulta_por_rango_y_dropea_lo_viejo()
    {
        Skip.IfNot(pg.Available, "Docker no disponible");
        var archive = Archive();
        await archive.EnsureSchemaAsync();

        var today = DateTimeOffset.UtcNow;
        var old = today.AddDays(-40);
        await archive.AppendAsync(new[]
        {
            Event("old-1", "dev-cold", old),
            Event("new-1", "dev-cold", today.AddMinutes(-5)),
            Event("new-2", "dev-cold", today.AddMinutes(-1)),
        });

        // Consulta por rango: solo los recientes (últimos 60 min), del más nuevo al más viejo.
        var recent = await archive.QueryAsync("dev-cold", today.AddMinutes(-60), today.AddMinutes(1));
        Assert.Equal(2, recent.Count);
        Assert.Equal("new-2", recent[0].EventId);

        // Idempotencia: reinsertar el mismo evento no duplica (ON CONFLICT).
        await archive.AppendAsync(new[] { Event("new-2", "dev-cold", today.AddMinutes(-1)) });
        var again = await archive.QueryAsync("dev-cold", today.AddMinutes(-60), today.AddMinutes(1));
        Assert.Equal(2, again.Count);

        // Retención: dropea la partición del evento viejo, conserva la de hoy.
        var dropped = await archive.DropPartitionsBeforeAsync(DateOnly.FromDateTime(today.UtcDateTime).AddDays(-1));
        Assert.Contains(PartitionName.ForDate(DateOnly.FromDateTime(old.UtcDateTime)), dropped);

        var afterDrop = await archive.QueryAsync("dev-cold", old.AddMinutes(-1), old.AddMinutes(1));
        Assert.Empty(afterDrop); // los datos viejos se fueron con la partición
    }
}
