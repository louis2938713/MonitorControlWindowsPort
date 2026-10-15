// Generates src/MonitorControl.App/Localization/Strings/<lang>.json from:
//  - the macOS translations (MonitorControl/UI/<lang>.lproj/Localizable.strings and Main.strings), using mac-sources.json,
//  - Windows-specific strings translated in overrides/<lang>.json.
// English (en.json) is the reference and is edited by hand.
//
// Usage (from the repository root): dotnet run tools/localization/convert-strings.cs
#:property TreatWarningsAsErrors=false
#:property RestorePackagesWithLockFile=false

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

var root = Directory.GetCurrentDirectory();
var stringsDir = Path.Combine(root, "src", "MonitorControl.App", "Localization", "Strings");
var toolDir = Path.Combine(root, "tools", "localization");
var macDir = Path.Combine(root, "MonitorControl", "UI");
string[] languages = ["cs", "de", "es", "fa", "fr", "hi", "hu", "it", "ja", "ko", "nl", "pl", "pt-BR", "pt-PT", "ru", "sk", "tr", "zh-Hans", "zh-Hant-TW"];

var english = ReadOrderedJson(Path.Combine(stringsDir, "en.json"));
var sources = ReadOrderedJson(Path.Combine(toolDir, "mac-sources.json"));
var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var exitCode = 0;

foreach (var key in english.Keys.Where(k => !sources.ContainsKey(k)))
{
    // Windows-only keys must be translated in every override file.
    foreach (var language in languages)
    {
        var overrides = ReadOrderedJson(Path.Combine(toolDir, "overrides", language + ".json"));
        if (!overrides.ContainsKey(key))
        {
            Console.Error.WriteLine($"{language}: missing translation for new key {key}");
            exitCode = 1;
        }
    }
}

foreach (var language in languages)
{
    var localizable = ParseStrings(Path.Combine(macDir, language + ".lproj", "Localizable.strings"));
    var main = ParseStrings(Path.Combine(macDir, language + ".lproj", "Main.strings"));
    var overrides = ReadOrderedJson(Path.Combine(toolDir, "overrides", language + ".json"));
    var output = new Dictionary<string, string>();
    var missing = new List<string>();
    foreach (var (key, englishText) in english)
    {
        string? text = null;
        if (overrides.TryGetValue(key, out var translated))
        {
            text = translated;
        }
        else if (sources.TryGetValue(key, out var source))
        {
            var parts = source.Split('|');
            text = parts[0] switch
            {
                "SAME" => englishText,
                var s when s.StartsWith("L:", StringComparison.Ordinal) => localizable.GetValueOrDefault(s[2..]),
                var s when s.StartsWith("M:", StringComparison.Ordinal) => main.GetValueOrDefault(s[2..] + ".title"),
                _ => null,
            };
            foreach (var replacement in parts.Skip(1))
            {
                var pair = replacement.Split("=>");
                text = text?.Replace(pair[0], pair[1], StringComparison.Ordinal);
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            missing.Add(key);
            continue;
        }

        output[key] = text;
    }

    using (var stream = File.Create(Path.Combine(stringsDir, language + ".json")))
    using (var writer = new Utf8JsonWriter(stream, options))
    {
        writer.WriteStartObject();
        foreach (var (key, value) in output)
        {
            writer.WriteString(key, value);
        }

        writer.WriteEndObject();
    }
    Console.WriteLine($"{language}: {output.Count}/{english.Count} strings{(missing.Count > 0 ? " (English fallback: " + string.Join(", ", missing) + ")" : string.Empty)}");
}

return exitCode;

static Dictionary<string, string> ReadOrderedJson(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var result = new Dictionary<string, string>();
    foreach (var property in document.RootElement.EnumerateObject())
    {
        if (!property.Name.StartsWith('_') && property.Value.ValueKind == JsonValueKind.String)
        {
            result[property.Name] = property.Value.GetString()!;
        }
    }

    return result;
}

// Parses an Apple .strings file: "key" = "value"; (keys are kept escaped, values are unescaped).
static Dictionary<string, string> ParseStrings(string path)
{
    var result = new Dictionary<string, string>();
    var pattern = new Regex("^\"((?:[^\"\\\\]|\\\\.)*)\"\\s*=\\s*\"((?:[^\"\\\\]|\\\\.)*)\";", RegexOptions.Multiline);
    foreach (Match match in pattern.Matches(File.ReadAllText(path)))
    {
        result[match.Groups[1].Value] = Regex.Unescape(match.Groups[2].Value.Replace("\\\"", "\"", StringComparison.Ordinal));
    }

    return result;
}
