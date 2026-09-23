using System.Text.Json.Serialization;

namespace McpGoogleDrive.Core.Setup;

public sealed record PickedFolder(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);
