using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using MonitorControl.App.Localization;
using MonitorControl.App.UI;
using MonitorControl.Core;
using MonitorControl.Core.Enums;
using MonitorControl.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace MonitorControl.App.Services;

/// <summary>
/// Updates through Velopack and GitHub Releases (replacement for Sparkle). Packages are verified by Velopack
/// (SHA checksums) and downloaded over HTTPS. Automatic checks run shortly after startup and then daily.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private readonly Prefs _prefs;
    private readonly Action _beforeRestart;
    private readonly DispatcherTimer _timer = new();
    private bool _checking;

    public UpdateService(Prefs prefs, Action beforeRestart)
    {
        _prefs = prefs;
        _beforeRestart = beforeRestart;
        _timer.Interval = FirstCheckDelay;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (_prefs.Bool(PrefKey.EnableAutomaticUpdateChecks))
            {
                await CheckAsync(userInitiated: false).ConfigureAwait(true);
            }
        };
    }

    public void Start() => _timer.Start();

    public void Dispose() => _timer.Stop();

    /// <summary>Checks for an update and offers to install it.</summary>
    public async Task CheckAsync(bool userInitiated)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            var manager = new UpdateManager(new GithubSource(AppInfo.UpdateRepositoryUrl, null, _prefs.Bool(PrefKey.IsBetaChannel)));
            if (!manager.IsInstalled)
            {
                if (userInitiated)
                {
                    AlertWindow.Show(Loc.T("Menu.CheckForUpdates").TrimEnd('…', '.'), Loc.T("Update.NotInstalled"));
                }

                return;
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(true);
            if (update is null)
            {
                Core.Log.Info("No update available");
                if (userInitiated)
                {
                    AlertWindow.Show(Loc.T("Update.UpToDate.Title"), Loc.F("Update.UpToDate.Message", App.Version));
                }

                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            Core.Log.Info($"Update {version} available");
            var answer = AlertWindow.Show(
                Loc.T("Update.Available.Title"),
                Loc.F("Update.Available.Message", version, App.Version),
                Loc.T("Update.Install"),
                Loc.T("Update.Later"));
            if (answer != 0)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(update).ConfigureAwait(true);
            _beforeRestart();
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
        {
            Core.Log.Warning("Update check failed", ex);
            if (userInitiated)
            {
                AlertWindow.Show(Loc.T("Update.Failed"), null);
            }
        }
        finally
        {
            _checking = false;
        }
    }
}
