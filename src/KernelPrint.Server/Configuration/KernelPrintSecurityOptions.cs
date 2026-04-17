namespace KernelPrint.Server.Configuration;

public sealed class KernelPrintClientCredential
{
    public string Id { get; set; } = "";

    /// <summary>
    /// Secret sent in the X-Api-Key header for this client.
    /// </summary>
    public string ApiKey { get; set; } = "";
}

public sealed class KernelPrintRateLimitOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum requests per client per window (in-memory; per instance).
    /// </summary>
    public int PermitLimit { get; set; } = 120;

    public int WindowSeconds { get; set; } = 60;
}

public sealed class KernelPrintSecurityOptions
{
    /// <summary>
    /// When set, requests must include this value in the X-Api-Key header.
    /// Ignored when <see cref="Clients"/> is non-empty (use clients instead).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional multi-client credentials. When non-empty, any matching <see cref="KernelPrintClientCredential.ApiKey"/> is accepted.
    /// </summary>
    public KernelPrintClientCredential[] Clients { get; set; } = [];

    /// <summary>
    /// When true, API key is required even in Development.
    /// </summary>
    public bool RequireApiKeyInDevelopment { get; set; }

    public KernelPrintRateLimitOptions RateLimit { get; set; } = new();
}
