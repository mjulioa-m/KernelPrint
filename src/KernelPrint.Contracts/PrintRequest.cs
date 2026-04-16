namespace KernelPrint.Contracts;

public sealed record PrintRequest
{
    public string? TemplateId { get; init; }
    public string? TemplateUrl { get; init; }
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    public PrintOptions Options { get; init; } = new();
}
