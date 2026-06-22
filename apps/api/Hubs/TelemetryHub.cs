using Atalaya.Api.Processing;
using Microsoft.AspNetCore.SignalR;

namespace Atalaya.Api.Hubs;

/// <summary>
/// Hub SignalR del camino caliente (ADR-002). Soporta <b>grupos por viewport</b> (AUD-008): el
/// cliente declara los dispositivos visibles con <see cref="SyncViewport"/> y solo recibe los
/// deltas de ese conjunto (escala el push por viewport, no por flota completa). Sin viewport, el
/// cliente recibe el firehose por <c>Clients.All</c>.
/// </summary>
public sealed class TelemetryHub(ViewportRegistry registry) : Hub
{
    /// <summary>
    /// Sincroniza el viewport del cliente: lo deja suscrito exactamente a los grupos de
    /// <paramref name="deviceIds"/> (añade los nuevos, quita los que salieron). Entra en modo
    /// viewport, así que el forwarder deja de mandarle el broadcast completo.
    /// </summary>
    public async Task SyncViewport(string[] deviceIds)
    {
        var next = new HashSet<string>(deviceIds);
        var prev = registry.Current(Context.ConnectionId);

        foreach (var id in next)
            if (!prev.Contains(id))
                await Groups.AddToGroupAsync(Context.ConnectionId, Group(id));

        foreach (var id in prev)
            if (!next.Contains(id))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(id));

        registry.Set(Context.ConnectionId, next);
    }

    /// <summary>Sale del modo viewport: vuelve al firehose (<c>Clients.All</c>).</summary>
    public async Task ClearViewport()
    {
        foreach (var id in registry.Current(Context.ConnectionId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(id));

        registry.Remove(Context.ConnectionId);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public static string Group(string deviceId) => $"device:{deviceId}";
}
