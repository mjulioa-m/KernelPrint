namespace KernelPrint.Engine.Configuration;

public sealed class KernelPrintTemplateSecurityOptions
{
    /// <summary>
    /// When true, only http/https template URLs are allowed (data: URLs are blocked).
    /// </summary>
    public bool BlockDataUrls { get; set; } = true;

    /// <summary>
    /// Allowed hostnames for absolute template URLs (case-insensitive).
    /// Example: ["localhost", "templates", "127.0.0.1"]
    /// </summary>
    public string[] AllowedHosts { get; set; } = ["localhost", "127.0.0.1", "templates"];

    /// <summary>
    /// Additional hosts allowed for subresources (fonts/CDNs) when <see cref="EnforceResourceHostAllowlist"/> is enabled.
    /// </summary>
    public string[] ExtraResourceAllowedHosts { get; set; } =
    [
        "fonts.googleapis.com",
        "fonts.gstatic.com"
    ];

    /// <summary>
    /// Allowed URL schemes for template navigation.
    /// </summary>
    public string[] AllowedSchemes { get; set; } = ["http", "https"];

    /// <summary>
    /// Maximum JSON payload size for Data (UTF-8 bytes).
    /// </summary>
    public int MaxDataJsonBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// Maximum navigation timeout for goto (ms).
    /// </summary>
    public int MaxNavigationTimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// Maximum PDF generation timeout (ms).
    /// </summary>
    public int MaxPdfTimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// Maximum time to wait for an explicit readiness signal from the template (ms).
    /// </summary>
    public int MaxReadySignalTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// When true, block third-party subresources unless the host is allowlisted.
    /// </summary>
    public bool EnforceResourceHostAllowlist { get; set; } = true;
}
