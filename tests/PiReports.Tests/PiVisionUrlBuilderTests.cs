using PiReports.Core.Capture;
using PiReports.Core.Model;

namespace PiReports.Tests;

public class PiVisionUrlBuilderTests
{
    [Fact]
    public void Adds_kiosk_and_time_range_inside_the_hash_route()
    {
        var url = PiVisionUrlBuilder.Build(new CaptureDefinition
        {
            DisplayUrl = "https://pivision/PIVision/#/Displays/12/Unit-3",
            StartTime = "*-24h",
            EndTime = "*",
            HideToolbar = true,
        });

        Assert.Equal("https://pivision/PIVision/#/Displays/12/Unit-3?mode=kiosk&HideToolbar=true&StartTime=%2A-24h&EndTime=%2A", url);
    }

    [Fact]
    public void Replaces_existing_parameters_case_insensitively_and_keeps_others()
    {
        var url = PiVisionUrlBuilder.Build(new CaptureDefinition
        {
            DisplayUrl = "https://pivision/PIVision/#/Displays/12/Unit-3?starttime=*-1h&rootpath=%5C%5CAF%5CPlant",
            StartTime = "t",
            HideToolbar = false,
        });

        Assert.Equal("https://pivision/PIVision/#/Displays/12/Unit-3?rootpath=%5C%5CAF%5CPlant&mode=kiosk&StartTime=t", url);
    }

    [Fact]
    public void Blank_settings_leave_the_display_defaults_alone()
    {
        var url = PiVisionUrlBuilder.Build(new CaptureDefinition
        {
            DisplayUrl = "https://pivision/PIVision/#/Displays/12/Unit-3?mode=kiosk",
            StartTime = " ",
            HideToolbar = false,
        });

        Assert.Equal("https://pivision/PIVision/#/Displays/12/Unit-3?mode=kiosk", url);
    }

    [Fact]
    public void Encodes_asset_paths()
    {
        var url = PiVisionUrlBuilder.Build(new CaptureDefinition
        {
            DisplayUrl = "https://pivision/PIVision/#/Displays/12/Unit-3",
            Asset = @"\\AF\Plant\Unit 3",
            HideToolbar = false,
        });

        Assert.EndsWith("Asset=%5C%5CAF%5CPlant%5CUnit%203", url);
    }

    [Fact]
    public void Handles_urls_with_a_normal_query_string()
    {
        var url = PiVisionUrlBuilder.Build(new CaptureDefinition
        {
            DisplayUrl = "https://pivision/PIVision/Displays/12?x=1#section",
            HideToolbar = false,
        });

        Assert.Equal("https://pivision/PIVision/Displays/12?x=1&mode=kiosk#section", url);
    }

    [Theory]
    [InlineData("pivision/PIVision")]
    [InlineData("file:///c:/temp/x.html")]
    [InlineData("")]
    public void Rejects_anything_but_absolute_http_urls(string displayUrl)
    {
        Assert.Throws<ArgumentException>(() => PiVisionUrlBuilder.Build(new CaptureDefinition { DisplayUrl = displayUrl }));
    }
}
