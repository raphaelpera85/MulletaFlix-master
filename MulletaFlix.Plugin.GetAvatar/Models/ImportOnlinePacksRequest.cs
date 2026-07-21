using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace MulletaFlix.Plugin.GetAvatar.Controllers
{
    /// <summary>
    /// Request model for importing online avatar packs.
    /// </summary>
    public class ImportOnlinePacksRequest
    {
        /// <summary>
        /// Gets or sets the selected packs to import.
        /// </summary>
        [JsonPropertyName("packs")]
        public List<JsonElement> Packs { get; set; } = new List<JsonElement>();
    }
}
