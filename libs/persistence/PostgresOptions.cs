namespace Atalaya.Persistence;

/// <summary>Configuración de Postgres. Sección "Postgres" / ConnectionStrings:Postgres.</summary>
public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}
