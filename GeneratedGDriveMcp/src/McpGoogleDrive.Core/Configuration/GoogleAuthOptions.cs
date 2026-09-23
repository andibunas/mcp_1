namespace McpGoogleDrive.Core.Configuration;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string ApplicationName { get; set; } = "McpGoogleDrive";

    /// <summary>
    /// API key for the Google Picker API, used only by the interactive folder-grant flow.
    /// Not required for the manual folder-ID entry fallback.
    /// </summary>
    public string PickerApiKey { get; set; } = string.Empty;
}
