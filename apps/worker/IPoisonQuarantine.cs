namespace Atalaya.Worker;

/// <summary>
/// Cuarentena forense de mensajes "veneno" (AUDIT §6.13): un cuerpo que no deserializa se descarta
/// (Ack, para no entrar en bucle), pero antes se guarda una copia cruda para análisis posterior
/// (¿qué firmware/dispositivo manda basura?). Best-effort: un fallo guardando NUNCA debe impedir el
/// Ack. Implementación real en GCS (modo Gcp); no-op en el resto (InMemory/Aws sin destino).
/// </summary>
public interface IPoisonQuarantine
{
    Task QuarantineAsync(ReadOnlyMemory<byte> payload, string reason, CancellationToken ct = default);
}

/// <summary>No-op: en InMemory/Aws no hay destino de cuarentena configurado.</summary>
public sealed class NullPoisonQuarantine : IPoisonQuarantine
{
    public Task QuarantineAsync(ReadOnlyMemory<byte> payload, string reason, CancellationToken ct = default)
        => Task.CompletedTask;
}
