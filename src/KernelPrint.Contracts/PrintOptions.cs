namespace KernelPrint.Contracts;

public sealed record PrintOptions
{
    public PdfPageFormat Format { get; init; } = PdfPageFormat.A4;
    public bool Landscape { get; init; }
    public MarginOptions Margins { get; init; } = new();
    public string? HeaderTemplateHtml { get; init; }
    public string? FooterTemplateHtml { get; init; }
    public WaitForOptions WaitFor { get; init; } = new();
}

public enum PdfPageFormat
{
    A4,
    Letter,
    Legal
}

public sealed record MarginOptions
{
    public string Top { get; init; } = "16mm";
    public string Right { get; init; } = "12mm";
    public string Bottom { get; init; } = "16mm";
    public string Left { get; init; } = "12mm";
}

public sealed record WaitForOptions
{
    public int NetworkIdleMs { get; init; } = 500;
    public int FontsReadyTimeoutMs { get; init; } = 5_000;
    public int MaxRenderTimeoutMs { get; init; } = 30_000;
}
