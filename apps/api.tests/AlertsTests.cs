using System.Net.Http.Json;
using Atalaya.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Integración de alertas como incidentes sin Docker (InMemory): un evento sobre el umbral abre
/// un incidente; cuando la señal se normaliza, el mismo incidente pasa a resuelto (AUD-016/p1).
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
    public async Task Un_evento_sobre_el_umbral_abre_un_incidente_y_al_normalizar_se_resuelve()
    {
        var ts = DateTimeOffset.UtcNow;

        // Temperatura crítica → abre incidente.
        await Ingest(new TelemetryEvent("hot-1", "dev-2", ts, 1, 19.5, -99.2, 60, 90, 75, 120));
        var open = await PollIncidentAsync("dev-2:engine-temp-high", IncidentStatus.Open);
        Assert.Equal("dev-2", open.DeviceId);
        Assert.Equal(AlertSeverity.Critical, open.Severity);

        // Temperatura normalizada (< 90) → resuelve el mismo incidente.
        await Ingest(new TelemetryEvent("cool-1", "dev-2", ts.AddSeconds(1), 2, 19.5, -99.2, 60, 90, 75, 70));
        var resolved = await PollIncidentAsync("dev-2:engine-temp-high", IncidentStatus.Resolved);
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
    }

    private async Task Ingest(TelemetryEvent e)
    {
        var resp = await _client.PostAsJsonAsync("/ingest", new[] { e });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<AlertIncident> PollIncidentAsync(string incidentId, IncidentStatus status)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var incidents = await _client.GetFromJsonAsync<List<AlertIncident>>("/api/alerts") ?? [];
            var match = incidents.FirstOrDefault(i => i.IncidentId == incidentId && i.Status == status);
            if (match is not null) return match;
            await Task.Delay(50);
        }
        Assert.Fail($"No apareció el incidente {incidentId} en estado {status}");
        return null!;
    }
}
