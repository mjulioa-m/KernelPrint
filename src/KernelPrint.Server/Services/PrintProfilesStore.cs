using KernelPrint.Contracts;
using Microsoft.Extensions.Configuration;

namespace KernelPrint.Server.Services;

/// <summary>
/// Loads named print profiles from configuration (<c>KernelPrint:Profiles</c>).
/// </summary>
public sealed class PrintProfilesStore
{
    public PrintProfilesStore(IConfiguration configuration)
    {
        var dict = new Dictionary<string, PrintOptions>(StringComparer.OrdinalIgnoreCase);
        var section = configuration.GetSection("KernelPrint:Profiles");
        foreach (var child in section.GetChildren())
        {
            dict[child.Key] = child.Get<PrintOptions>() ?? new PrintOptions();
        }

        Profiles = dict;
    }

    public IReadOnlyDictionary<string, PrintOptions> Profiles { get; }

    /// <summary>
    /// Merges profile defaults with request options (request non-default JSON fields win).
    /// </summary>
    public static PrintRequest MergeRequestProfile(PrintRequest request, PrintProfilesStore store)
    {
        if (string.IsNullOrWhiteSpace(request.Profile))
        {
            return request;
        }

        if (!store.Profiles.TryGetValue(request.Profile, out var profileOptions))
        {
            return request;
        }

        var mergedOptions = JsonMergeHelper.MergeJsonObjects(profileOptions, request.Options);
        return request with { Options = mergedOptions };
    }
}
