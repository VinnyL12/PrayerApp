using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrayerApp.Client.Services;

public static class ApiJson
{
    /// <summary>Matches the API: camelCase properties, enums as names.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
