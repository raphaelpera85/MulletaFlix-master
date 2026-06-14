using System.Text.Json;

namespace Jellyfin.Extensions.Json;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
