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

    // Shortened from https://github.com/<owner>/BatteryStrip/releases.atom.
    private const string Releases = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xml:lang="en-US">
          <link type="text/html" rel="alternate" href="https://github.com/o/BatteryStrip/releases"/>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/o/BatteryStrip/releases/tag/v0.4.0-beta.1"/>
            <title>BatteryStrip 0.4.0-beta.1</title>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/o/BatteryStrip/releases/tag/v0.3.1"/>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/someone-else/BatteryStrip/releases/tag/v9.0.0"/>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/o/BatteryStrip/releases/tag/nightly"/>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/o/BatteryStrip/releases/tag/v0.3.0"/>
          </entry>
        </feed>
        """;

    [Fact]
    public void Stable_users_get_the_newest_stable_release()
    {
        AvailableUpdate? update = UpdateChecker.Newest(Releases, V("0.3.0"), "o/BatteryStrip");
        Assert.Equal("0.3.1", update!.Version.ToString());
        Assert.Equal("https://github.com/o/BatteryStrip/releases/download/v0.3.1/BatteryStrip-0.3.1.exe", update.ExeUrl);
        Assert.Equal("https://github.com/o/BatteryStrip/releases/download/v0.3.1/BatteryStrip-0.3.1.sha256.txt", update.ChecksumUrl);
    }

    [Fact]
    public void Beta_users_also_get_betas()
    {
        AvailableUpdate? update = UpdateChecker.Newest(Releases, V("0.3.0-beta.1"), "o/BatteryStrip");
        Assert.Equal("0.4.0-beta.1", update!.Version.ToString());
        Assert.EndsWith("/v0.4.0-beta.1/BatteryStrip-0.4.0-beta.1.exe", update.ExeUrl);
    }

    [Fact]
    public void Nothing_when_up_to_date_and_never_from_another_repository()
    {
        Assert.Null(UpdateChecker.Newest(Releases, V("0.4.0"), "o/BatteryStrip"));
        Assert.Null(UpdateChecker.Newest(Releases, V("0.3.0"), "x/BatteryStrip"));
        Assert.Null(UpdateChecker.Newest("<feed></feed>", V("0.3.0"), "o/BatteryStrip"));
    }
}
