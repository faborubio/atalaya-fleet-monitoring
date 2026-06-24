using System.Diagnostics.Metrics;

namespace Atalaya.Worker;

/// <summary>
/// Métricas OpenTelemetry del worker (SAD §8: observabilidad). Histograma de latencia del
/// pipeline (evento→procesado) y contadores RED-ish. En dev se exportan por consola; en
/// producción el mismo Meter se exporta vía OTLP a un colector + Prometheus/Grafana.
/// </summary>
public sealed class WorkerMetrics
{
    public const string MeterName = "Atalaya.Worker";

    private readonly Histogram<double> _pipelineLatency;
    private readonly Counter<long> _processed;
    private readonly Counter<long> _duplicates;
    private readonly Counter<long> _alertsRaised;
    private readonly Counter<long> _archived;
    private readonly Counter<long> _poison;

    public WorkerMetrics(IMeterFactory factory)
    {
        var meter = factory.Create(MeterName);
        _pipelineLatency = meter.CreateHistogram<double>(
            "atalaya.pipeline.latency", unit: "ms",
            description: "Latencia evento→procesado en el worker");
        _processed = meter.CreateCounter<long>("atalaya.events.processed");
        _duplicates = meter.CreateCounter<long>("atalaya.events.duplicates");
        _alertsRaised = meter.CreateCounter<long>("atalaya.alerts.raised");
        _archived = meter.CreateCounter<long>("atalaya.telemetry.archived");
        _poison = meter.CreateCounter<long>("atalaya.events.poison");
    }

    public void RecordLatency(double milliseconds) => _pipelineLatency.Record(milliseconds);
    public void AddProcessed(long count) => _processed.Add(count);
    public void AddDuplicates(long count) => _duplicates.Add(count);
    public void AddAlerts(long count) => _alertsRaised.Add(count);
    public void AddArchived(long count) => _archived.Add(count);
    /// <summary>Mensajes descartados por venenosos (cuerpo que nunca deserializará).</summary>
    public void AddPoison(long count) => _poison.Add(count);
}
