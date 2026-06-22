using System.Collections.Concurrent;

namespace Atalaya.Api.Processing;

/// <summary>
/// Registro de las conexiones en <b>modo viewport</b> (AUD-008): clientes que solo quieren los
/// deltas de los dispositivos visibles, no todo el firehose. Guarda, por conexión, el conjunto de
/// dispositivos suscritos. El forwarder lo usa para excluir a estas conexiones del broadcast
/// (<c>Clients.AllExcept</c>) y mandarles únicamente sus grupos <c>device:{id}</c>.
/// <para>Quien no entra en modo viewport sigue recibiendo el firehose por <c>Clients.All</c>
/// (cero cambio de comportamiento por defecto).</para>
/// </summary>
public sealed class ViewportRegistry
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _byConnection = new();

    /// <summary>Conexiones actualmente en modo viewport (a excluir del broadcast).</summary>
    public IReadOnlyList<string> ConnectionsInViewportMode() => _byConnection.Keys.ToArray();

    /// <summary>
    /// Unión de todos los dispositivos que alguna conexión viewport sigue. El forwarder solo
    /// envía a esos grupos (los demás no tienen suscriptores: evita sends a grupos vacíos).
    /// </summary>
    public IReadOnlySet<string> SubscribedDevices()
    {
        var union = new HashSet<string>();
        foreach (var set in _byConnection.Values)
            union.UnionWith(set);
        return union;
    }

    /// <summary>Dispositivos suscritos por una conexión (vacío si no está en modo viewport).</summary>
    public IReadOnlySet<string> Current(string connectionId) =>
        _byConnection.TryGetValue(connectionId, out var set) ? set : new HashSet<string>();

    public void Set(string connectionId, HashSet<string> deviceIds) =>
        _byConnection[connectionId] = deviceIds;

    public void Remove(string connectionId) => _byConnection.TryRemove(connectionId, out _);
}
