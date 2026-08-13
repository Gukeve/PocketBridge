using System.Globalization;

namespace PocketBridge.Infrastructure;

public static class ScreenshotFilenameFormatter
{
    private static readonly string[] DateTokens = ["yyyy", "MM", "dd", "HH", "mm", "ss"];

    public static string Format(string template, string deviceName, DateTime timestamp)
    {
        var result = template;
        foreach (var token in DateTokens)
            result = result.Replace(token, timestamp.ToString(token, CultureInfo.InvariantCulture), StringComparison.Ordinal);

        result = result.Replace("<device>", Sanitize(deviceName), StringComparison.OrdinalIgnoreCase);
        return Sanitize(result);
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
