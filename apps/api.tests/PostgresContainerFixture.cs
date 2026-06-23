using Testcontainers.PostgreSql;
using Xunit;

namespace Atalaya.Api.Tests;

/// <summary>
/// Levanta un Postgres real en un contenedor (Testcontainers, SAD Fase 3 / AUD-015 F) para probar
/// el código que el modo InMemory no cubre: particionado, <c>unnest</c>, <c>ON CONFLICT</c>,
/// retención por <c>DROP PARTITION</c> y la máquina de incidentes en SQL. Si Docker no está
/// disponible, los tests se saltan (no fallan).
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>Si Docker está disponible y el contenedor arrancó.</summary>
    public bool Available { get; private set; }

    public string ConnectionString => _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            // Construir el builder valida Docker, por eso va dentro del try.
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            Available = true;
        }
        catch
        {
            Available = false; // sin Docker: los tests de integración se saltan
        }
    }

    public Task DisposeAsync() =>
        _container is not null ? _container.DisposeAsync().AsTask() : Task.CompletedTask;
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
