using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atalaya.Contracts;

namespace Atalaya.Persistence;

/// <summary>
/// Deriva la clave del objeto del data lake S3 de forma <b>determinista por contenido</b>
/// (AUD-015 punto 3): <c>raw/yyyy/MM/dd/{sha256(body)}.json</c>. Así, una reentrega del mismo
/// lote (at-least-once) produce la misma clave y el <c>PutObject</c> sobrescribe en vez de crear
/// un duplicado. La fecha de partición sale del evento más antiguo del lote (también determinista).
/// </summary>
public static class RawEventKey
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Serializa el lote (cuerpo del objeto) y calcula su clave idempotente.</summary>
    public static (string Key, string Body) Build(IReadOnlyList<TelemetryEvent> events)
    {
        var body = JsonSerializer.Serialize(events, Json);
        return (KeyFor(events, body), body);
    }

    /// <summary>Clave determinista para un cuerpo ya serializado (separada para poder testearla).</summary>
    public static string KeyFor(IReadOnlyList<TelemetryEvent> events, string body)
    {
        var date = events.Min(e => e.Ts).UtcDateTime; // determinista por contenido
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        // '/' escapado: en un format de fecha es separador de cultura (podría salir '-').
        return $"raw/{date:yyyy'/'MM'/'dd}/{hash}.json";
    }
}
