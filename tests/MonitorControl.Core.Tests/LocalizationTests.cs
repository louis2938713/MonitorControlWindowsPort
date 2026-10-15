using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonitorControl.Core.Tests;

/// <summary>Checks the UI string files of every language (they live in the App project).</summary>
public sealed partial class LocalizationTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string StringsDirectory = Path.Combine(Root, "src", "MonitorControl.App", "Localization", "Strings");

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(StringsDirectory, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name != "en")
            {
                data.Add(name);
            }
        }

        return data;
    }

    [Fact]
    public void AllTwentyLanguagesAreShipped() => Assert.Equal(20, Directory.GetFiles(StringsDirectory, "*.json").Length);

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_HasExactlyTheEnglishKeys(string language)
    {
        var english = Load("en");
        var translated = Load(language);

        Assert.Empty(english.Keys.Except(translated.Keys));
        Assert.Empty(translated.Keys.Except(english.Keys));
        Assert.All(translated, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_KeepsFormatPlaceholders(string language)
    {
        var english = Load("en");
        foreach (var (key, text) in Load(language))
        {
            Assert.True(
                Placeholders(english[key]).SetEquals(Placeholders(text)),
                $"{language}: placeholders differ for {key}");
        }
    }

    [Fact]
    public void EveryKeyUsedByTheApp_Exists()
    {
        var english = Load("en");
        var sources = Directory.GetFiles(Path.Combine(Root, "src", "MonitorControl.App"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();
        var allText = string.Join('\n', sources);

        var used = UsedKeyPattern().Matches(allText).Select(m => m.Groups["key"].Value).ToHashSet();
        var unknownKeys = used.Where(k => !english.ContainsKey(k)).ToList();
        Assert.True(unknownKeys.Count == 0, "Keys used but missing from en.json: " + string.Join(", ", unknownKeys));
    }

    private static Dictionary<string, string> Load(string language)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(StringsDirectory, language + ".json")));
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
    }

    private static HashSet<string> Placeholders(string text) => PlaceholderPattern().Matches(text).Select(m => m.Value).ToHashSet();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MonitorControl.Windows.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"(?:Loc\.[TF]\(""|loc:Tr |\bnew\(""|Loc\.T\([^""]*""|: "")(?<key>[A-Z][A-Za-z]+(?:\.[A-Za-z]+)+)")]
    private static partial Regex UsedKeyPattern();
}
