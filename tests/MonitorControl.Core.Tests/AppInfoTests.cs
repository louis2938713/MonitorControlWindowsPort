using MonitorControl.Core;

namespace MonitorControl.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void Urls_AreHttps()
    {
        string[] urls = [AppInfo.WebsiteUrl, AppInfo.DonateUrl, AppInfo.ContributorsUrl, AppInfo.UpdateRepositoryUrl];
        Assert.All(urls, url => Assert.StartsWith("https://", url, StringComparison.Ordinal));
    }
}
