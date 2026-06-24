using System.Text.Json;
using Atalaya.Api.Services;
using Atalaya.Contracts;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Pruebas del troceo de eventos en mensajes Pub/Sub (<see cref="GcpPubSubBatchPublisher.BuildMessages"/>,
/// ADR-013). El contrato: cada mensaje es un array JSON de ≤N eventos (mismo formato que el worker
/// espera) y no se pierde ni se reordena nada respecto al orden de ingesta.
/// </summary>
public sealed class GcpMessageBuilderTests
{
    private static TelemetryEvent Event(int i) =>
        new($"evt-{i}", $"dev-{i % 100}", DateTimeOffset.UtcNow, i,
            19.4, -99.1, 40, 90, 80, 85);

    private static IReadOnlyList<TelemetryEvent> Events(int n) =>
        Enumerable.Range(0, n).Select(Event).ToList();

    [Fact]
    public void Sin_eventos_no_genera_mensajes()
    {
        Assert.Empty(GcpPubSubBatchPublisher.BuildMessages(Events(0), maxEventsPerMessage: 100));
    }

    [Fact]
    public void Trocea_respetando_el_maximo_de_eventos_por_mensaje()
    {
        // 250 eventos / 100 por mensaje = 3 mensajes (100 + 100 + 50).
        var messages = GcpPubSubBatchPublisher.BuildMessages(Events(250), maxEventsPerMessage: 100);

        Assert.Equal(3, messages.Count);
        var sizes = messages
            .Select(m => JsonSerializer.Deserialize<TelemetryEvent[]>(m.Data.ToStringUtf8())!.Length)
            .ToList();
        Assert.Equal(new[] { 100, 100, 50 }, sizes);
    }

    [Fact]
    public void No_pierde_ni_reordena_eventos()
    {
        var original = Events(333);

        var roundtrip = GcpPubSubBatchPublisher.BuildMessages(original, maxEventsPerMessage: 7)
            .SelectMany(m => JsonSerializer.Deserialize<TelemetryEvent[]>(m.Data.ToStringUtf8())!)
            .ToList();

        Assert.Equal(original.Count, roundtrip.Count);
        Assert.Equal(original.Select(e => e.EventId), roundtrip.Select(e => e.EventId));
    }
}
