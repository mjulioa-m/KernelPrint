namespace KernelPrint.Contracts;

public sealed record PrintPreviewRequest
{
    public string? TemplateId { get; init; }
    public string? TemplateUrl { get; init; }
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    public PrintOptions Options { get; init; } = new();

    /// <summary>
    /// Viewport width for capture (clamped by the engine).
    /// </summary>
    public int MaxWidth { get; init; } = 1280;

    /// <summary>
    /// Viewport height for capture (clamped by the engine).
    /// </summary>
    public int MaxHeight { get; init; } = 2048;
}
