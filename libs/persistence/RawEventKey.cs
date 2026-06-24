using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atalaya.Contracts;

namespace Atalaya.Persistence;

/// <summary>
/// Deriva la clave del objeto del data lake de forma <b>determinista por contenido</b>
/// (AUD-015 punto 3): <c>raw/yyyy/MM/dd/{sha256(body)}.json</c>. Así, una reentrega del mismo
/// lote (at-least-once) produce la misma clave y la subida sobrescribe en vez de crear un
/// duplicado. La fecha de partición sale del evento más antiguo del lote (también determinista).
/// <para>
/// El cuerpo se escribe como <b>NDJSON</b> (un evento JSON por línea, AUD-024 / fase G4): es el
/// formato que <b>BigQuery</b> lee con una <i>external table</i> <c>NEWLINE_DELIMITED_JSON</c>
/// directamente sobre el bucket. (También es el formato line-delimited que consume Athena, así que
/// el camino S3 heredado sigue siendo válido.) El hash sobre el cuerpo NDJSON sigue siendo
/// determinista, de modo que la clave idempotente no cambia de semántica.
/// </para>
/// </summary>
public static class RawEventKey
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Serializa el lote como NDJSON (un evento por línea) y calcula su clave idempotente.</summary>
    public static (string Key, string Body) Build(IReadOnlyList<TelemetryEvent> events)
    {
        // NDJSON: una línea por evento → BigQuery (NEWLINE_DELIMITED_JSON) lee cada línea como una fila.
        var body = string.Join('\n', events.Select(e => JsonSerializer.Serialize(e, Json)));
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
