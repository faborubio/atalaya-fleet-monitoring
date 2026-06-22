using System.Net.Http.Json;
using Atalaya.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Verifica los grupos por viewport (AUD-008) con un cliente SignalR real contra el test server
/// (transporte long-polling sobre el handler en memoria): un cliente que declara su viewport solo
/// recibe los deltas de esos dispositivos; el resto del firehose no le llega.
/// </summary>
public sealed class ViewportTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ViewportTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Telemetry:Transport", "InMemory");
            b.UseSetting("Ingest:Token", "");
        });
    }

    [Fact]
    public async Task En_modo_viewport_solo_llegan_los_deltas_suscritos()
    {
        var handler = _factory.Server.CreateHandler();
        var received = new List<string>();
        var gate = new object();

        await using var hub = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/telemetry", o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => handler;
            })
            .Build();

        hub.On<DeviceState[]>("devicesUpdated", batch =>
        {
            lock (gate)
                received.AddRange(batch.Select(d => d.DeviceId));
        });

        await hub.StartAsync();

        // Entra en modo viewport: solo quiere "keep". Se espera a que el server aplique los grupos.
        await hub.InvokeAsync("SyncViewport", new[] { "keep" });

        var client = _factory.CreateClient();
        var ts = DateTimeOffset.UtcNow;
        var batch = new[]
        {
            new TelemetryEvent("k-1", "keep", ts, 1, 19.4, -99.1, 50, 90, 80, 70),
            new TelemetryEvent("o-1", "other", ts, 1, 19.5, -99.2, 60, 90, 75, 72),
        };
        (await client.PostAsJsonAsync("/ingest", batch)).EnsureSuccessStatusCode();

        // Da tiempo a procesar y empujar; luego comprueba que solo llegó "keep".
        for (var i = 0; i < 50; i++)
        {
            lock (gate)
                if (received.Contains("keep")) break;
            await Task.Delay(50);
        }
        await Task.Delay(200); // margen para detectar un "other" indebido

        lock (gate)
        {
            Assert.Contains("keep", received);
            Assert.DoesNotContain("other", received);
        }

        await hub.StopAsync();
    }
}
