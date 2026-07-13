using Ax206Display.Config.Models;
using Ax206Display.Rendering.Widgets;

namespace Ax206Display.Tests.Rendering;

public class WidgetDesignItemTests
{
    [Fact]
    public void FromConfig_ThenToConfig_RoundTripsAllFields()
    {
        var config = new WidgetConfig
        {
            Id = "w1",
            Type = "stat",
            X = 10,
            Y = 20,
            Width = 100,
            Height = 50,
            ZOrder = 3,
        };
        config.Settings["dataKey"] = "system.cpu.load";
        config.Settings["decimals"] = 1;

        var item = WidgetDesignItem.FromConfig(config);
        var roundTripped = item.ToConfig();

        Assert.Equal(config.Id, roundTripped.Id);
        Assert.Equal(config.Type, roundTripped.Type);
        Assert.Equal(config.X, roundTripped.X);
        Assert.Equal(config.Y, roundTripped.Y);
        Assert.Equal(config.Width, roundTripped.Width);
        Assert.Equal(config.Height, roundTripped.Height);
        Assert.Equal(config.ZOrder, roundTripped.ZOrder);
        Assert.Equal("system.cpu.load", roundTripped.Settings["dataKey"]?.GetValue<string>());
        Assert.Equal(1, roundTripped.Settings["decimals"]?.GetValue<int>());
    }

    [Fact]
    public void EditingTheItem_DoesNotMutateTheOriginalConfig()
    {
        var config = new WidgetConfig { Id = "w1", Type = "text", X = 0, Y = 0, Width = 10, Height = 10 };
        config.Settings["text"] = "original";

        var item = WidgetDesignItem.FromConfig(config);
        item.X = 99;
        item.SetSetting("text", "edited");

        Assert.Equal(0, config.X);
        Assert.Equal("original", config.Settings["text"]?.GetValue<string>());
    }

    [Fact]
    public void SetSetting_WithNullOrEmpty_RemovesTheKey()
    {
        var item = new WidgetDesignItem { Id = "w1", Type = "text" };
        item.SetSetting("text", "hello");

        item.SetSetting("text", null);

        Assert.Null(item.GetSetting("text"));
    }

    [Fact]
    public void GetIntSetting_WhenAbsent_ReturnsDefault()
    {
        var item = new WidgetDesignItem { Id = "w1", Type = "stat" };

        Assert.Equal(2, item.GetIntSetting("decimals", 2));
    }

    [Fact]
    public void GetIntSetting_AfterSet_ReturnsStoredValue()
    {
        var item = new WidgetDesignItem { Id = "w1", Type = "stat" };
        item.SetIntSetting("decimals", 3);

        Assert.Equal(3, item.GetIntSetting("decimals", 0));
    }

    [Fact]
    public void GetDoubleSetting_WhenAbsent_ReturnsDefault()
    {
        var item = new WidgetDesignItem { Id = "w1", Type = "clock" };

        Assert.Equal(1.0, item.GetDoubleSetting("fontScale", 1.0));
    }

    [Fact]
    public void GetDoubleSetting_AfterSet_ReturnsStoredValue()
    {
        var item = new WidgetDesignItem { Id = "w1", Type = "clock" };
        item.SetDoubleSetting("fontScale", 1.25);

        Assert.Equal(1.25, item.GetDoubleSetting("fontScale", 1.0));
    }

    [Fact]
    public void GetBoolSetting_WhenAbsent_ReturnsDefault()
    {
        var item = new WidgetDesignItem { Id = "w1", Type = "clock" };

        Assert.False(item.GetBoolSetting("bold", false));
    }

    [Fact]
    public void GetBoolSetting_AfterSet_ReturnsStoredValue()
    {
        var item = new WidgetDesignItem { Id = "w1", Type = "clock" };
        item.SetBoolSetting("bold", true);

        Assert.True(item.GetBoolSetting("bold", false));
    }
}
