using MonitorControl.App.Localization;
using MonitorControl.App.UI;
using MonitorControl.Core.Platform;

namespace MonitorControl.App.Services;

/// <summary>Dialogs raised by the core logic.</summary>
internal sealed class Alerts : IAlerts
{
    public bool AskDisableGammaAfterInterference() =>
        AlertWindow.Show(
            Loc.T("Alert.GammaInterference.Title"),
            Loc.T("Alert.GammaInterference.Message"),
            Loc.T("Alert.GammaInterference.QuitOther"),
            Loc.T("Alert.GammaInterference.DisableGamma")) == 1;

    public void ShowIncompatiblePreviousVersion() =>
        AlertWindow.Show(Loc.T("Alert.IncompatibleVersion.Title"), Loc.T("Alert.IncompatibleVersion.Message"));

    public void ShowSafeMode() =>
        AlertWindow.Show(Loc.T("Alert.SafeMode.Title"), Loc.T("Alert.SafeMode.Message"));
}
