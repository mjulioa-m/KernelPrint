namespace KernelPrint.Server.Configuration;

public sealed class KernelPrintServerOptions
{
    /// <summary>
    /// Maximum request body size in bytes (Kestrel). Should align with template MaxDataJsonBytes plus JSON overhead.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 768 * 1024;

    /// <summary>
    /// Server-side request timeout for print/preview endpoints (milliseconds). 0 disables (not recommended).
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 120_000;

    /// <summary>
    /// Seconds to send in Retry-After when rejecting due to concurrency saturation.
    /// </summary>
    public int ConcurrencyRetryAfterSeconds { get; set; } = 5;

    /// <summary>
    /// Optional absolute URL for an HTTP GET/HEAD readiness probe (e.g. template root).
    /// </summary>
    public string? ReadinessHttpProbeUrl { get; set; }

    /// <summary>
    /// Timeout for readiness HTTP probe (milliseconds).
    /// </summary>
    public int ReadinessHttpProbeTimeoutMs { get; set; } = 3_000;

    /// <summary>
    /// Maximum PDF size (bytes) allowed when returning application/json with embedded Base64 (JsonWithPdf).
    /// </summary>
    public int MaxJsonResponsePdfBytes { get; set; } = 4 * 1024 * 1024;
}
