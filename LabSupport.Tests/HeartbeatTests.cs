using System;
using Xunit;
using LabSupport.Agent.Services;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class HeartbeatTests
{
    [Fact]
    public void CollectHeartbeatData_ShouldProvideValidSystemTelemetry()
    {
        var service = new HeartbeatService("127.0.0.1", 8080);
        var hb = service.CollectHeartbeatData();

        Assert.NotNull(hb);
        Assert.False(string.IsNullOrWhiteSpace(hb.Hostname));
        Assert.False(string.IsNullOrWhiteSpace(hb.IpAddress));
        Assert.False(string.IsNullOrWhiteSpace(hb.MacAddress));
        Assert.True(hb.RamTotalMb > 0, "Total RAM should be greater than 0 MB.");
        Assert.True(hb.RamUsageMb >= 0, "Used RAM should be non-negative.");
        Assert.InRange(hb.CpuUsage, 0.0, 100.0);
        Assert.False(string.IsNullOrWhiteSpace(hb.ScreenshotBase64));
    }

    [Fact]
    public void InputSimulator_ShouldHandleMouseAndKeyboardPayloadsGracefully()
    {
        var simulator = new InputSimulator();

        var mouse = new MouseInputPayload
        {
            Action = MouseAction.LeftClick,
            NormalizedX = 0.5,
            NormalizedY = 0.5
        };
        bool mouseRes = simulator.SimulateMouse(mouse);
        Assert.True(mouseRes);

        var key = new KeyboardInputPayload
        {
            Action = KeyboardAction.Shortcut,
            Key = "WIN+D"
        };
        bool keyRes = simulator.SimulateKeyboard(key);
        Assert.True(keyRes);
    }
}
