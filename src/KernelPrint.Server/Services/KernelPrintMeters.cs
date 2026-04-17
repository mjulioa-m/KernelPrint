using System.Diagnostics.Metrics;

namespace KernelPrint.Server.Services;

public sealed class KernelPrintMeters
{
    private readonly Meter _meter;

    public KernelPrintMeters()
    {
        _meter = new Meter("KernelPrint.Server", "1.0.0");
        PrintLatencyMs = _meter.CreateHistogram<double>("kernelprint.print.latency.ms");
        PrintRequestCount = _meter.CreateCounter<long>("kernelprint.print.request.count");
        PrintOutcomeCount = _meter.CreateCounter<long>("kernelprint.print.outcome.count");
        PreviewRequestCount = _meter.CreateCounter<long>("kernelprint.preview.request.count");
    }

    public Histogram<double> PrintLatencyMs { get; }
    public Counter<long> PrintRequestCount { get; }
    public Counter<long> PrintOutcomeCount { get; }
    public Counter<long> PreviewRequestCount { get; }

    public void RecordPrintOutcome(string outcome, string? clientId = null)
    {
        PrintOutcomeCount.Add(1, new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("client_id", clientId ?? "anonymous"));
    }
}
