namespace KernelPrint.Engine.Configuration;

public sealed class KernelPrintEngineOptions
{
    public string TemplateBaseUrl { get; set; } = "http://localhost:3000";
    public int MaxConcurrency { get; set; } = 4;
    public int BrowserLaunchTimeoutMs { get; set; } = 30_000;
}
