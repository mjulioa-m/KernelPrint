namespace KernelPrint.Server.Configuration;

public sealed class KernelPrintSecurityOptions
{
    /// <summary>
    /// When set, requests must include this value in the X-Api-Key header.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// When true, API key is required even in Development.
    /// </summary>
    public bool RequireApiKeyInDevelopment { get; set; }
}
