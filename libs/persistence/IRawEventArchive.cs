using Atalaya.Contracts;

namespace Atalaya.Persistence;

/// <summary>
/// Data lake de eventos crudos (ADR-007): fuente de verdad fría e inmutable, base de Athena en
/// AWS real. La implementación S3 vive en el worker (único escritor); aquí solo la interfaz y un
/// no-op para el modo dev sin Docker / tests, donde no hay S3.
/// </summary>
public interface IRawEventArchive
{
    /// <summary>Crea el bucket si falta (idempotente).</summary>
    Task EnsureBucketAsync(CancellationToken ct = default);

    /// <summary>Vuelca el lote crudo a una partición <c>raw/yyyy/mm/dd/</c>.</summary>
    Task AppendAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default);
}

/// <summary>No-op: data lake desactivado (sin S3, p. ej. dev sin Docker o tests).</summary>
public sealed class NullRawEventArchive : IRawEventArchive
{
    public Task EnsureBucketAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task AppendAsync(IReadOnlyList<TelemetryEvent> events, CancellationToken ct = default) =>
        Task.CompletedTask;
}
