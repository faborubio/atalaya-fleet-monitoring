using System.Text;
using System.Text.Json;
using Atalaya.Api.Services;
using Atalaya.Contracts;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Pruebas del planificador de lotes SNS (<see cref="SnsBatchPublisher.PlanBatches"/>, AUD-010).
/// El riesgo real es superar los límites de SNS PublishBatch (≤10 mensajes y ≤256 KB por lote):
/// si el lote se pasa, SNS lo rechaza y los eventos se perderían. Aquí se fija ese contrato.
/// </summary>
public sealed class SnsBatchPlannerTests
{
    private const int MaxBatchEntries = 10;
    private const int MaxBatchBytes = 240 * 1024;

    private static TelemetryEvent Event(int i) =>
        new($"evt-{i}", $"dev-{i % 100}", DateTimeOffset.UtcNow, i,
            19.4, -99.1, 40, 90, 80, 85);

    private static IReadOnlyList<TelemetryEvent> Events(int n) =>
        Enumerable.Range(0, n).Select(Event).ToList();

    [Fact]
    public void Sin_eventos_no_genera_lotes()
    {
        Assert.Empty(SnsBatchPublisher.PlanBatches(Events(0), maxEventsPerMessage: 100));
    }

    [Fact]
    public void Agrupa_mensajes_respetando_el_maximo_de_10_por_lote()
    {
        // 250 eventos / 10 por mensaje = 25 mensajes → 3 lotes (10 + 10 + 5).
        var batches = SnsBatchPublisher.PlanBatches(Events(250), maxEventsPerMessage: 10);

        Assert.All(batches, b => Assert.True(b.Count <= MaxBatchEntries));
        Assert.Equal(25, batches.Sum(b => b.Count)); // 25 mensajes en total
        Assert.Equal(3, batches.Count);
    }

    [Fact]
    public void Ningun_lote_supera_el_limite_de_bytes_de_sns()
    {
        // 100 ev/mensaje × muchos eventos: el límite que manda es el de bytes, no el de 10 entradas.
        var batches = SnsBatchPublisher.PlanBatches(Events(5_000), maxEventsPerMessage: 100);

        Assert.NotEmpty(batches);
        foreach (var batch in batches)
        {
            Assert.True(batch.Count <= MaxBatchEntries);
            var total = batch.Sum(m => Encoding.UTF8.GetByteCount(m));
            Assert.True(total <= MaxBatchBytes, $"lote de {total} bytes supera {MaxBatchBytes}");
        }
    }

    [Fact]
    public void No_pierde_ni_reordena_eventos()
    {
        var original = Events(333);

        var roundtrip = SnsBatchPublisher.PlanBatches(original, maxEventsPerMessage: 7)
            .SelectMany(batch => batch)
            .SelectMany(msg => JsonSerializer.Deserialize<TelemetryEvent[]>(msg)!)
            .ToList();

        Assert.Equal(original.Count, roundtrip.Count);
        Assert.Equal(original.Select(e => e.EventId), roundtrip.Select(e => e.EventId));
    }
}
