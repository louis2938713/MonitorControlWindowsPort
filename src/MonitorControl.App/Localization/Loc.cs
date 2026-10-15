using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace MonitorControl.App.Localization;

/// <summary>
/// UI strings. Each language is an embedded <c>Strings/&lt;lang&gt;.json</c> file (stable symbolic keys → text).
/// Fallback chain: exact culture (pt-BR) → parent (pt) → English → the key itself (never shown for known keys).
/// Raises <see cref="PropertyChanged"/> on language change so XAML bindings update live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string DefaultLanguage = "en";

    /// <summary>Supported languages, with their name in their own language.</summary>
    public static IReadOnlyList<(string Code, string NativeName)> Languages { get; } =
    [
        ("cs", "Čeština"),
        ("de", "Deutsch"),
        ("en", "English"),
        ("es", "Español"),
        ("fa", "فارسی"),
        ("fr", "Français"),
        ("hi", "हिन्दी"),
        ("hu", "Magyar"),
        ("it", "Italiano"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("nl", "Nederlands"),
        ("pl", "Polski"),
        ("pt-BR", "Português (Brasil)"),
        ("pt-PT", "Português (Portugal)"),
        ("ru", "Русский"),
        ("sk", "Slovenčina"),
        ("tr", "Türkçe"),
        ("zh-Hans", "简体中文"),
        ("zh-Hant-TW", "繁體中文（台灣）"),
    ];

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _english = new Dictionary<string, string>();

    private Loc()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static event EventHandler? LanguageChanged;

    public static Loc Instance { get; } = new();

    /// <summary>Language code in use (one of <see cref="Languages"/>).</summary>
    public static string Language { get; private set; } = DefaultLanguage;

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);

    public static FlowDirection FlowDirection => Culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Indexer used by XAML bindings: <c>{Binding [Key], Source={x:Static loc:Loc.Instance}}</c>.</summary>
    public string this[string key] => Get(key);

    /// <summary>Applies a language. Empty means "follow Windows".</summary>
    public static void SetLanguage(string? preferred)
    {
        var code = Resolve(string.IsNullOrEmpty(preferred) ? CultureInfo.CurrentUICulture.Name : preferred);
        Instance._english = Load(DefaultLanguage);
        Instance._strings = Load(code);
        Language = code;
        Culture = CultureInfo.GetCultureInfo(code switch
        {
            "zh-Hant-TW" => "zh-TW",
            "zh-Hans" => "zh-CN",
            _ => code,
        });
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(FlowDirection)));
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Translated text for a key.</summary>
    public static string T(string key) => Instance.Get(key);

    /// <summary>Translated text with <c>{0}</c>-style arguments, formatted with the UI culture.</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, Instance.Get(key), args);

    /// <summary>Maps a culture name (fr-CA, pt-BR, zh-TW…) to a supported language code.</summary>
    public static string Resolve(string cultureName)
    {
        if (string.IsNullOrEmpty(cultureName))
        {
            return DefaultLanguage;
        }

        var exact = Languages.FirstOrDefault(l => l.Code.Equals(cultureName, StringComparison.OrdinalIgnoreCase));
        if (exact.Code is not null)
        {
            return exact.Code;
        }

        var lower = cultureName.ToLowerInvariant();
        if (lower.StartsWith("zh", StringComparison.Ordinal))
        {
            return lower.Contains("hant", StringComparison.Ordinal) || lower.EndsWith("-tw", StringComparison.Ordinal)
                || lower.EndsWith("-hk", StringComparison.Ordinal) || lower.EndsWith("-mo", StringComparison.Ordinal)
                ? "zh-Hant-TW"
                : "zh-Hans";
        }

        if (lower.StartsWith("pt", StringComparison.Ordinal))
        {
            return lower == "pt-br" ? "pt-BR" : "pt-PT";
        }

        var neutral = lower.Split('-')[0];
        var match = Languages.FirstOrDefault(l => l.Code.Equals(neutral, StringComparison.OrdinalIgnoreCase));
        return match.Code ?? DefaultLanguage;
    }

    /// <summary>Loads the strings of a language from the embedded resources (empty if missing).</summary>
    public static IReadOnlyDictionary<string, string> Load(string code)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var cached))
            {
                return cached;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Strings.{code}.json");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                if (parsed is not null)
                {
                    foreach (var (key, value) in parsed)
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            result[key] = value;
                        }
                    }
                }
            }

            Cache[code] = result;
            return result;
        }
    }

    private string Get(string key) =>
        _strings.TryGetValue(key, out var text) ? text
        : _english.TryGetValue(key, out var english) ? english
        : key;
}
