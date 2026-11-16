using System.IO;
using System.Text;
using BatteryStrip.App;
using Xunit;

namespace BatteryStrip.Tests;

public class SettingsTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("en                         # ru, en, or empty to follow Windows", "en")]
    [InlineData("off # no", "off")]
    [InlineData("", "")]
    public void Option_values_lose_an_inline_comment(string raw, string value)
    {
        Assert.Equal(value, Settings.StripComment(raw));
    }

    [Fact]
    public void A_line_copied_from_an_old_readme_still_works_and_names_keep_their_hash()
    {
        string path = Path.Combine(Path.GetTempPath(), "BatteryStrip.SettingsTests." + System.Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "language = en   # ru, en, or empty\nname compx:25A7:FA7C = Mouse #2\n", Encoding.UTF8);
            var settings = new Settings(path);
            Assert.Equal("en", settings.Language);
            Assert.Equal("Mouse #2", settings.DeviceNames["compx:25A7:FA7C"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
