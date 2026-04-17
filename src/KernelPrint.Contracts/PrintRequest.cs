namespace KernelPrint.Contracts;

public sealed record PrintRequest
{
    public string? TemplateId { get; init; }
    public string? TemplateUrl { get; init; }

    /// <summary>
    /// Optional profile name resolved from server configuration (<c>KernelPrint:Profiles</c>).
    /// </summary>
    public string? Profile { get; init; }

    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    public PrintOptions Options { get; init; } = new();

    /// <summary>
    /// When <see cref="PrintResponseMode.JsonWithPdf"/>, the API may return JSON with Base64 PDF if under size limits.
    /// </summary>
    public PrintResponseMode ResponseMode { get; init; } = PrintResponseMode.PdfOnly;
}
