using BatteryStrip.App;
using Xunit;

namespace BatteryStrip.Tests;

public class UpdatesTests
{
    private static AppVersion V(string text) => AppVersion.Parse(text)!.Value;

    [Theory]
    [InlineData("0.3.0-beta.1", "0.3.0-beta.2")]
    [InlineData("0.3.0-beta.2", "0.3.0-beta.10")]
    [InlineData("0.3.0-beta.9", "0.3.0")]
    [InlineData("0.3.0", "0.3.1")]
    [InlineData("0.9.9", "1.0.0")]
    [InlineData("v1.2.3", "1.10.0")]
    public void Versions_are_ordered_like_semver(string older, string newer)
    {
        Assert.True(V(older).CompareTo(V(newer)) < 0);
        Assert.True(V(newer).CompareTo(V(older)) > 0);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("latest")]
    [InlineData("")]
    public void Bad_tags_are_ignored(string tag)
    {
        Assert.Null(AppVersion.Parse(tag));
    }

    private const string Releases = """
        [
          { "tag_name": "v0.4.0-beta.1", "draft": false, "prerelease": true,
            "assets": [ { "browser_download_url": "https://github.com/o/BatteryStrip/releases/download/v0.4.0-beta.1/BatteryStrip-0.4.0-beta.1.exe" },
                        { "browser_download_url": "https://github.com/o/BatteryStrip/releases/download/v0.4.0-beta.1/BatteryStrip-0.4.0-beta.1.sha256.txt" } ] },
          { "tag_name": "v0.3.1", "draft": false, "prerelease": false,
            "assets": [ { "browser_download_url": "https://github.com/o/BatteryStrip/releases/download/v0.3.1/BatteryStrip-0.3.1.exe" },
                        { "browser_download_url": "https://github.com/o/BatteryStrip/releases/download/v0.3.1/BatteryStrip-0.3.1.sha256.txt" } ] },
          { "tag_name": "v0.5.0", "draft": true, "prerelease": false,
            "assets": [ { "browser_download_url": "https://github.com/o/BatteryStrip/releases/download/v0.5.0/BatteryStrip-0.5.0.exe" },
                        { "browser_download_url": "https://github.com/o/BatteryStrip/releases/download/v0.5.0/BatteryStrip-0.5.0.sha256.txt" } ] },
          { "tag_name": "v0.3.0", "draft": false, "prerelease": false, "assets": [] }
        ]
        """;

    [Fact]
    public void Stable_users_get_the_newest_stable_release()
    {
        AvailableUpdate? update = UpdateChecker.Newest(Releases, V("0.3.0"));
        Assert.Equal("0.3.1", update!.Version.ToString());
        Assert.EndsWith("/BatteryStrip-0.3.1.exe", update.ExeUrl);
        Assert.EndsWith("/BatteryStrip-0.3.1.sha256.txt", update.ChecksumUrl);
    }

    [Fact]
    public void Beta_users_also_get_betas_but_never_drafts()
    {
        Assert.Equal("0.4.0-beta.1", UpdateChecker.Newest(Releases, V("0.3.0-beta.1"))!.Version.ToString());
    }

    [Fact]
    public void Nothing_when_up_to_date_or_without_files()
    {
        Assert.Null(UpdateChecker.Newest(Releases, V("0.4.0")));
        Assert.Null(UpdateChecker.Newest("""[ { "tag_name": "v9.0.0", "draft": false, "assets": [] } ]""", V("0.3.0")));
    }
}
