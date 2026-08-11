namespace PocketBridge.Core.Models;

public sealed record AppSettings
{
    public string? ToolsDirectory { get; init; }
    public string Language { get; init; } = "ru-RU";
}
