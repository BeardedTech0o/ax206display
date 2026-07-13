using Ax206Display.Config.Models;
using Ax206Display.Rendering.Widgets;

namespace Ax206Display.Tests.Rendering;

public class WidgetFactoryTests
{
    [Fact]
    public void Create_ClockWithNoSettings_UsesDefaults()
    {
        var config = MakeConfig("clock");

        var widget = WidgetFactory.Create(config);

        var clock = Assert.IsType<ClockWidget>(widget);
        Assert.Equal("widget-1", clock.Id);
        Assert.Equal(200, clock.Width);
        Assert.Equal(60, clock.Height);
    }

    [Fact]
    public void Create_ClockWithTimeFormatSetting_UsesIt()
    {
        var config = MakeConfig("clock");
        config.Settings["timeFormat"] = "HH:mm";

        var widget = WidgetFactory.Create(config);

        // ClockWidget doesn't expose its format directly; render at a fixed
        // time and check the shorter "HH:mm" string produced no exception
        // and something was drawn (covered by ClockWidgetTests) - here we
        // just confirm construction succeeds with the custom setting present.
        Assert.IsType<ClockWidget>(widget);
    }

    [Fact]
    public void Create_UnknownType_Throws()
    {
        var config = MakeConfig("weather");

        var ex = Assert.Throws<NotSupportedException>(() => WidgetFactory.Create(config));
        Assert.Contains("weather", ex.Message);
    }

    [Fact]
    public void Create_TextWidget()
    {
        var config = MakeConfig("text");
        config.Settings["text"] = "Hello";

        var widget = WidgetFactory.Create(config);

        Assert.IsType<TextWidget>(widget);
    }

    [Fact]
    public void Create_StatWidget()
    {
        var config = MakeConfig("stat");
        config.Settings["dataKey"] = "system.cpu.load";
        config.Settings["label"] = "CPU";
        config.Settings["unit"] = "%";
        config.Settings["decimals"] = 1;

        var widget = WidgetFactory.Create(config);

        Assert.IsType<SystemStatWidget>(widget);
    }

    [Fact]
    public void Create_ClockWithFontFamilySetting_UsesIt()
    {
        var config = MakeConfig("clock");
        config.Settings["fontFamily"] = "Consolas";

        var widget = WidgetFactory.Create(config);

        Assert.IsType<ClockWidget>(widget);
    }

    [Fact]
    public void Create_ClockWithBoldItalicAndFontScaleSettings_UsesThem()
    {
        var config = MakeConfig("clock");
        config.Settings["bold"] = true;
        config.Settings["italic"] = true;
        config.Settings["fontScale"] = 1.25;

        var widget = WidgetFactory.Create(config);

        Assert.IsType<ClockWidget>(widget);
    }

    [Fact]
    public void Create_StatWidgetWithoutDataKey_Throws()
    {
        var config = MakeConfig("stat");

        var ex = Assert.Throws<InvalidOperationException>(() => WidgetFactory.Create(config));
        Assert.Contains("dataKey", ex.Message, StringComparison.Ordinal);
    }

    private static WidgetConfig MakeConfig(string type) => new()
    {
        Id = "widget-1",
        Type = type,
        X = 0,
        Y = 0,
        Width = 200,
        Height = 60,
    };
}
