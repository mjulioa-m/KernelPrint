namespace KernelPrint.Contracts;

/// <summary>
/// JSON envelope when <see cref="PrintRequest.ResponseMode"/> is <see cref="PrintResponseMode.JsonWithPdf"/>.
/// </summary>
public sealed record PrintPdfJsonResponse
{
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
    public required string PdfBase64 { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public PrintTimings Timings { get; init; } = new();
}
