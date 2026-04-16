namespace KernelPrint.Engine.Configuration;

public sealed class KernelPrintEngineOptions
{
    public string TemplateBaseUrl { get; set; } = "http://localhost:3000";
    public int MaxConcurrency { get; set; } = 4;
    public int BrowserLaunchTimeoutMs { get; set; } = 30_000;

    public KernelPrintTemplateSecurityOptions Templates { get; set; } = new();

    /// <summary>
    /// Recycle the shared Chromium browser after this many successful print jobs (0 disables).
    /// </summary>
    public int BrowserRecycleAfterJobs { get; set; } = 200;

    /// <summary>
    /// Recycle the shared Chromium browser after this many minutes (0 disables).
    /// </summary>
    public int BrowserRecycleAfterMinutes { get; set; } = 60;
}
