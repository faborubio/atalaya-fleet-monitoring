using System.Text.Json.Serialization;

namespace Atalaya.Contracts;

/// <summary>Severidad de una alerta por umbral (SAD §6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertSeverity
{
    Warning,
    Critical,
}

/// <summary>Estado de un incidente de alerta.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentStatus
{
    Open,
    Resolved,
}

/// <summary>
/// Incidente de alerta (AUD-016/p1): a diferencia del modelo por-evento anterior, una condición
/// que cruza un umbral abre <b>un</b> incidente por <c>(deviceId, rule)</c> que vive hasta que la
/// señal se normaliza (con histéresis). Solo las <b>transiciones</b> (abrir/escalar/resolver)
/// generan notificación. <see cref="IncidentId"/> = <c>{deviceId}:{rule}</c>.
/// </summary>
public sealed record AlertIncident(
    string IncidentId,
    string DeviceId,
    string Rule,
    AlertSeverity Severity,
    IncidentStatus Status,
    double Value,
    DateTimeOffset OpenedAt,
    DateTimeOffset UpdatedAt,
    string Message)
{
    public static string Id(string deviceId, string rule) => $"{deviceId}:{rule}";
}
