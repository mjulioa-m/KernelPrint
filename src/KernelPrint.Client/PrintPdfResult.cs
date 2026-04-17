using KernelPrint.Contracts;

namespace KernelPrint.Client;

public sealed class PrintPdfResult
{
    public required byte[] Bytes { get; init; }

    public required string ContentType { get; init; }

    public string? FileName { get; init; }

    public PrintTimings Timings { get; init; } = new();

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
