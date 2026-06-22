using System.Net.Http.Json;
using Atalaya.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Integración del camino de alertas sin Docker (WebApplicationFactory, transporte InMemory):
/// ingesta de un evento que cruza un umbral → procesador aplica reglas → read model de alertas
/// → snapshot en <c>/api/alerts</c>.
/// </summary>
public sealed class AlertsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AlertsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Telemetry:Transport", "InMemory");
                b.UseSetting("Ingest:Token", "");
            })
            .CreateClient();
    }

    [Fact]
    public async Task Un_evento_sobre_el_umbral_genera_una_alerta_en_el_read_model()
    {
        var ts = DateTimeOffset.UtcNow;
        var batch = new[]
        {
            // Normal: no debe alertar.
            new TelemetryEvent("ok-1", "dev-1", ts, 1, 19.4, -99.1, 50, 90, 80, 70),
            // Temperatura crítica: debe alertar.
            new TelemetryEvent("hot-1", "dev-2", ts, 1, 19.5, -99.2, 60, 90, 75, 120),
        };

        var post = await _client.PostAsJsonAsync("/ingest", batch);
        post.EnsureSuccessStatusCode();

        var alerts = await PollAlertsAsync(expected: 1);

        var alert = Assert.Single(alerts);
        Assert.Equal("dev-2", alert.DeviceId);
        Assert.Equal("engine-temp-high", alert.Rule);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
    }

    private async Task<IReadOnlyList<Alert>> PollAlertsAsync(int expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var alerts = await _client.GetFromJsonAsync<List<Alert>>("/api/alerts") ?? [];
            if (alerts.Count >= expected) return alerts;
            await Task.Delay(50);
        }
        return await _client.GetFromJsonAsync<List<Alert>>("/api/alerts") ?? [];
    }
}
