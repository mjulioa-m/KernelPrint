namespace KernelPrint.Engine.Runtime;

internal static class HostAllowlistMatcher
{
    public static bool IsHostAllowed(string host, IEnumerable<string> allowedHosts)
    {
        foreach (var allowed in allowedHosts)
        {
            if (string.Equals(allowed, "*", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
