using System.Net;
using System.Net.Http.Json;
using Atalaya.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Test de integración del camino caliente, sin Docker (WebApplicationFactory).
/// Verifica: ingesta → bus → procesador → dedup → read model.
/// </summary>
public sealed class HotPathTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Ingest_actualiza_el_read_model_y_deduplica()
    {
        var ts = DateTimeOffset.UtcNow;
        var dup = new TelemetryEvent("evt-1", "dev-1", ts, 1, 19.4, -99.1, 40, 90, 80, 85);
        var batch = new[]
        {
            dup,
            dup, // duplicado: debe ignorarse (ADR-006)
            new TelemetryEvent("evt-2", "dev-1", ts.AddSeconds(1), 2, 19.5, -99.2, 42, 92, 79, 86),
            new TelemetryEvent("evt-3", "dev-2", ts, 1, 25.0, -100.0, 10, 10, 50, 70),
        };

        var post = await _client.PostAsJsonAsync("/ingest", batch);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);

        var states = await PollDevicesAsync(expected: 2);

        Assert.Equal(2, states.Count);
        var dev1 = Assert.Single(states, s => s.DeviceId == "dev-1");
        Assert.Equal(2, dev1.Seq); // quedó el último seq, no el duplicado
    }

    private async Task<IReadOnlyList<DeviceState>> PollDevicesAsync(int expected)
    {
        // El procesamiento es asíncrono (BackgroundService); reintentamos brevemente.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var states = await _client.GetFromJsonAsync<List<DeviceState>>("/api/devices")
                         ?? new List<DeviceState>();
            if (states.Count >= expected)
                return states;
            await Task.Delay(50);
        }

        return await _client.GetFromJsonAsync<List<DeviceState>>("/api/devices")
               ?? new List<DeviceState>();
    }
}
