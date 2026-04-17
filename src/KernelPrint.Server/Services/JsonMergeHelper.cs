using System.Text.Json;
using System.Text.Json.Nodes;

namespace KernelPrint.Server.Services;

internal static class JsonMergeHelper
{
    private static readonly JsonSerializerOptions MergeSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
    };

    public static T MergeJsonObjects<T>(T baseValue, T overlayValue)
    {
        var baseNode = JsonSerializer.SerializeToNode(baseValue, MergeSerializerOptions) as JsonObject;
        var overlayNode = JsonSerializer.SerializeToNode(overlayValue, MergeSerializerOptions) as JsonObject;
        if (baseNode is null || overlayNode is null)
        {
            return overlayValue;
        }

        MergeInto(baseNode, overlayNode);
        return JsonSerializer.Deserialize<T>(baseNode, MergeSerializerOptions)!;
    }

    private static void MergeInto(JsonObject target, JsonObject overlay)
    {
        foreach (var kvp in overlay)
        {
            if (kvp.Value is JsonObject overlayObj && target[kvp.Key] is JsonObject targetObj)
            {
                MergeInto(targetObj, overlayObj);
                continue;
            }

            target[kvp.Key] = kvp.Value?.DeepClone();
        }
    }
}
