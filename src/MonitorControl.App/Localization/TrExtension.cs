using System.Windows.Data;
using System.Windows.Markup;

namespace MonitorControl.App.Localization;

/// <summary>
/// XAML markup extension for translated text that updates live when the language changes:
/// <c>Text="{loc:Tr Settings.General.StartAtLogin}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
