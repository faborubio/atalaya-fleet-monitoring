using System.Net;
using System.Net.Http.Json;
using Atalaya.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Integración del camino frío sin Docker (WebApplicationFactory, transporte InMemory):
/// ingesta → archivo de telemetría → consulta histórica en <c>/api/history</c>.
/// </summary>
public sealed class HistoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HistoryTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("Telemetry:Transport", "InMemory");
                b.UseSetting("Ingest:Token", "");
                b.UseSetting("Auth:Mode", "Disabled"); // cubre el camino frío, no la auth
            })
            .CreateClient();
    }

    [Fact]
    public async Task El_historico_devuelve_la_telemetria_archivada_del_dispositivo()
    {
        var ts = DateTimeOffset.UtcNow;
        var batch = new[]
        {
            new TelemetryEvent("h-1", "dev-hist", ts.AddSeconds(-2), 1, 19.4, -99.1, 50, 90, 80, 70),
            new TelemetryEvent("h-2", "dev-hist", ts.AddSeconds(-1), 2, 19.4, -99.1, 55, 90, 79, 71),
            new TelemetryEvent("h-3", "otro-dev", ts, 1, 19.5, -99.2, 60, 90, 75, 72),
        };

        var post = await _client.PostAsJsonAsync("/ingest", batch);
        post.EnsureSuccessStatusCode();

        var points = await PollHistoryAsync("dev-hist", expected: 2);

        Assert.Equal(2, points.Count);
        Assert.All(points, p => Assert.Equal("dev-hist", p.DeviceId));
        // Orden descendente por ts: el más reciente primero.
        Assert.Equal("h-2", points[0].EventId);
    }

    [Fact]
    public async Task El_historico_exige_deviceId()
    {
        var resp = await _client.GetAsync("/api/history");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task La_serie_downsampled_agrupa_en_a_lo_sumo_buckets_intervalos()
    {
        // 12 eventos repartidos en ~24 min; con buckets=4 sobre 60 min el intervalo es de 15 min →
        // a lo sumo 4 puntos, y el conteo total agrupado debe ser 12 (downsampling, no pérdida).
        var ts = DateTimeOffset.UtcNow;
        var batch = Enumerable.Range(0, 12)
            .Select(i => new TelemetryEvent(
                $"d-{i}", "dev-ds", ts.AddMinutes(-i * 2), i, 19.4, -99.1, 40 + i, 90, 80, 70))
            .ToArray();

        (await _client.PostAsJsonAsync("/ingest", batch)).EnsureSuccessStatusCode();

        // Espera a que la ingesta InMemory archive los 12 (vía la consulta cruda).
        await PollHistoryAsync("dev-ds", expected: 12);

        var series = await _client.GetFromJsonAsync<List<TelemetryBucket>>(
            "/api/history/series?deviceId=dev-ds&minutes=60&buckets=4") ?? [];

        Assert.NotEmpty(series);
        Assert.True(series.Count <= 4, $"esperaba ≤4 buckets, hubo {series.Count}");
        Assert.Equal(12, series.Sum(b => b.Count)); // todos los eventos quedan representados
        // Ascendente por tiempo y métricas como promedio (rango plausible de speedKmh sembrado).
        Assert.True(series.SequenceEqual(series.OrderBy(b => b.Ts)));
        Assert.All(series, b => Assert.InRange(b.SpeedKmh, 40, 52));
    }

    private async Task<IReadOnlyList<TelemetryEvent>> PollHistoryAsync(string deviceId, int expected)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var points = await _client.GetFromJsonAsync<List<TelemetryEvent>>(
                $"/api/history?deviceId={deviceId}&minutes=60") ?? [];
            if (points.Count >= expected) return points;
            await Task.Delay(50);
        }
        return await _client.GetFromJsonAsync<List<TelemetryEvent>>(
            $"/api/history?deviceId={deviceId}&minutes=60") ?? [];
    }
}
