using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using MonitorControl.App.Localization;
using MonitorControl.Core;

namespace MonitorControl.App.UI.Settings;

/// <summary>About page. Port of AboutPrefsViewController.swift.</summary>
internal sealed partial class AboutViewModel(SettingsContext context)
{
    public string VersionText => $"{Loc.T("About.Version")} {App.Version} {Loc.T("About.Build")} {App.BuildNumber}";

    public string CopyrightText => $"{Loc.T("About.Copyright").TrimEnd()} {DateTime.Now.Year}";

    public string Maintainers => "@the0neyouseek (Guillaume B.)\n@JoniVR (Joni Van Roost)\n@waydabber (István T.)";

    [RelayCommand]
    private void CheckForUpdates() => context.CheckForUpdates();

    [RelayCommand]
    private static void OpenDonate() => Open(AppInfo.DonateUrl);

    [RelayCommand]
    private static void OpenWebsite() => Open(AppInfo.WebsiteUrl);

    [RelayCommand]
    private static void OpenContributors() => Open(AppInfo.ContributorsUrl);

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
