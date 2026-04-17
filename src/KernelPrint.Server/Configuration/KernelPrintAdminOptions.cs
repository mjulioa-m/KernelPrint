namespace KernelPrint.Server.Configuration;

public sealed class KernelPrintAdminOptions
{
    /// <summary>
    /// When set, POST /admin/pool/reset requires header X-Admin-Key with this value.
    /// </summary>
    public string? ApiKey { get; set; }
}
