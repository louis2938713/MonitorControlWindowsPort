namespace MonitorControl.Core;

/// <summary>Application-wide constants (identity, links). Kept in one place so they are easy to change.</summary>
public static class AppInfo
{
    public const string Name = "MonitorControl";

    /// <summary>Settings from builds older than this are considered incompatible and get reset.</summary>
    public const int MinPreviousBuildNumber = 6262;

    public const string WebsiteUrl = "https://monitorcontrol.app";
    public const string DonateUrl = "https://opencollective.com/monitorcontrol/donate";
    public const string ContributorsUrl = "https://github.com/MonitorControl/MonitorControl/graphs/contributors";
    /// <summary>Repository whose GitHub Releases carry the Windows installers and update packages.</summary>
    public const string UpdateRepositoryUrl = "https://github.com/Witch-Cat/MonitorControlWindowsPort";
}
