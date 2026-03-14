using System.Text.Json.Serialization;
using Sunrise.Shared.Enums.Beatmaps;

namespace Sunrise.Shared.Objects.Serializable.Events;

public class BeatmapStatusChanged
{
    [JsonPropertyName("beatmap_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BeatmapId { get; set; }

    [JsonPropertyName("beatmap_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BeatmapHash { get; set; }

    [JsonPropertyName("new_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BeatmapStatusWeb? NewStatus { get; set; } = null;
}