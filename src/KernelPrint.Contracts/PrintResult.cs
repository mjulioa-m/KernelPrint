namespace KernelPrint.Contracts;

public sealed record PrintResult
{
    public required byte[] Bytes { get; init; }
    public required string ContentType { get; init; }
    public string? FileName { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public PrintTimings Timings { get; init; } = new();
}

public sealed record PrintTimings
{
    public long TotalMs { get; init; }
    public long RenderMs { get; init; }
}
