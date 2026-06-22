using System.Text.Json.Serialization;

namespace Atalaya.Contracts;

/// <summary>Severidad de una alerta por umbral (SAD §6, tabla <c>alerts</c>).</summary>
/// <remarks>Serializa como string ("Warning"/"Critical") en todo el cableado (Redis, SignalR,
/// /api/alerts) para que el frontend reciba el nombre, no el índice.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertSeverity
{
    Warning,
    Critical,
}

/// <summary>
/// Alerta por umbral disparada en el procesamiento (SAD §1.3: reglas, no ML). Es un derivado
/// de un <see cref="TelemetryEvent"/>; alimenta el read model <c>alerts</c> y se notifica en
/// vivo (ADR-002).
/// <para><see cref="AlertId"/> es determinista (<c>{eventId}:{rule}</c>) para que la inserción
/// sea idempotente bajo el at-least-once de SQS (ADR-006): reprocesar el mismo evento no duplica
/// la alerta.</para>
/// </summary>
public sealed record Alert(
    string AlertId,
    string DeviceId,
    string Rule,
    AlertSeverity Severity,
    double Value,
    DateTimeOffset Ts,
    string Message);
