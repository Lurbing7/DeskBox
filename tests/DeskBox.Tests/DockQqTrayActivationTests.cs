namespace DeskBox.Tests;

public sealed class DockQqTrayActivationTests
{
    [Theory]
    [InlineData(3u)]
    [InlineData(19u)]
    public void RegisteredElectronTrayReceivesClickWithItsActualIconId(uint id)
    {
        var result = QqTrayCallbackFixture.Run(id);
        Assert.True(result.Delivered);
        Assert.Equal(1, result.Activated);
    }

    [Fact]
    public void AmbiguousTrayIconsAreNotClicked()
    {
        var result = QqTrayCallbackFixture.Run(secondIcon: true);
        Assert.False(result.Delivered);
        Assert.Equal(0, result.Activated);
    }

    [Fact]
    public void UnregisteredTrayWindowIsNotClicked()
    {
        var result = QqTrayCallbackFixture.Run(registerIcon: false);
        Assert.False(result.Delivered);
        Assert.Equal(0, result.Activated);
    }

    [Fact]
    public void ReadOnlyLookupDoesNotClickTheRegisteredTray()
    {
        var result = QqTrayCallbackFixture.Run(invoke: false);
        Assert.True(result.Delivered);
        Assert.Equal(0, result.Activated);
    }

    [Fact]
    public void LookupRejectsAnUnavailableProcess() =>
        Assert.False(DockQqTrayActivation.TryInvoke(0, null));

    [Theory]
    [InlineData(41u, 41u, "Electron_NotifyIconHostWindow", true)]
    [InlineData(41u, 42u, "Electron_NotifyIconHostWindow", false)]
    [InlineData(0u, 0u, "Electron_NotifyIconHostWindow", false)]
    [InlineData(41u, 41u, "Chrome_WidgetWin_1", false)]
    [InlineData(41u, 41u, "Qt51514WxTrayIconMessageWindowClass", false)]
    public void TrayMustBelongToTheRequestedProcessAndKnownProtocol(uint expected, uint actual, string name, bool supported) =>
        Assert.Equal(supported, DockQqTrayActivation.IsSupportedTrayWindow(expected, actual, name));
}
