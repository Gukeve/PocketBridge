using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace PocketBridge.App.Localization;

public sealed class LocalizationService
{
    private readonly ResourceManager _resources = new("PocketBridge.App.Resources.Strings", typeof(LocalizationService).Assembly);
    public static LocalizationService Current { get; private set; } = new("ru-RU");

    public LocalizationService(string language)
    {
        Culture = CultureInfo.GetCultureInfo(NormalizeLanguage(language));
    }

    public CultureInfo Culture { get; }
    public string this[string key] => _resources.GetString(key, Culture) ?? key;
    public string Format(string key, params object?[] arguments) => string.Format(Culture, this[key], arguments);

    public static void Initialize(string language)
    {
        Current = new LocalizationService(language);
        CultureInfo.CurrentUICulture = Current.Culture;
    }

    public static string NormalizeLanguage(string? language) => language switch
    {
        "en-US" => "en-US",
        "zh-CN" => "zh-CN",
        _ => "ru-RU"
    };
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;
    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationService.Current[Key];
}
