using System.Security.Cryptography;
using System.Text;
using KernelPrint.Server.Configuration;

namespace KernelPrint.Server.Auth;

public static class ApiKeyResolution
{
    public const string ClientIdItemKey = "KernelPrintClientId";

    public static bool TryValidateApiKey(
        string? providedKey,
        KernelPrintSecurityOptions security,
        out string? clientId)
    {
        clientId = null;
        if (string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        if (security.Clients.Length > 0)
        {
            foreach (var client in security.Clients)
            {
                if (string.IsNullOrEmpty(client.ApiKey))
                {
                    continue;
                }

                if (FixedTimeEquals(providedKey, client.ApiKey))
                {
                    clientId = string.IsNullOrWhiteSpace(client.Id) ? "client" : client.Id;
                    return true;
                }
            }

            return false;
        }

        if (string.IsNullOrEmpty(security.ApiKey))
        {
            return false;
        }

        if (!FixedTimeEquals(providedKey, security.ApiKey))
        {
            return false;
        }

        clientId = "default";
        return true;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    public static bool IsApiKeyRequired(IHostEnvironment env, KernelPrintSecurityOptions security)
    {
        if (security.Clients.Length > 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(security.ApiKey) &&
               (!env.IsDevelopment() || security.RequireApiKeyInDevelopment);
    }
}
