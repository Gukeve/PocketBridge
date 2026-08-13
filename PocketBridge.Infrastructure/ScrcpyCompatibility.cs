namespace PocketBridge.Infrastructure;

public static class ScrcpyCompatibility
{
    public const int MinimumApiLevel = 21;

    public static int? ParseApiLevel(string output) =>
        int.TryParse(output.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var apiLevel)
            ? apiLevel
            : null;

    public static bool IsSupported(int apiLevel) => apiLevel >= MinimumApiLevel;
}
