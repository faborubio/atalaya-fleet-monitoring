using Microsoft.AspNetCore.SignalR;

namespace Atalaya.Api.Hubs;

/// <summary>
/// Hub SignalR del camino caliente (ADR-002). El cliente se suscribe al grupo de los
/// dispositivos visibles —no a toda la flota— para que el push escale por viewport.
/// </summary>
public sealed class TelemetryHub : Hub
{
    public Task Subscribe(string deviceId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Group(deviceId));

    public Task Unsubscribe(string deviceId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(deviceId));

    public static string Group(string deviceId) => $"device:{deviceId}";
}
