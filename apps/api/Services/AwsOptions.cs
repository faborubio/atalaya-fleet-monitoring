namespace Atalaya.Api.Services;

/// <summary>Configuración de acceso a AWS (LocalStack en dev). Sección "Aws".</summary>
public sealed class AwsOptions
{
    public string ServiceUrl { get; set; } = "http://localhost:4566";
    public string Region { get; set; } = "us-east-1";
    public string TopicName { get; set; } = "atalaya-telemetry";
}
