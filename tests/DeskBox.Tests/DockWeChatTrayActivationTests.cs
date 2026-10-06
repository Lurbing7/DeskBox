namespace DeskBox.Tests;

public sealed class DockWeChatTrayActivationTests
{
    [Fact]
    public void RegisteredWeChatTrayReceivesItsCustomActivationCallback() =>
        Assert.True(WeChatTrayCallbackFixture.Run());

    [Theory]
    [InlineData(41u, 41u, "Qt51514WxTrayIconMessageWindowClass", true)]
    [InlineData(41u, 42u, "Qt51514WxTrayIconMessageWindowClass", false)]
    [InlineData(0u, 0u, "Qt51514WxTrayIconMessageWindowClass", false)]
    [InlineData(41u, 41u, "Qt51514QWindowIcon", false)]
    [InlineData(41u, 41u, "Qt6111WxTrayIconMessageWindowClass", false)]
    [InlineData(41u, 41u, "Shell_TrayWnd", false)]
    public void CallbackOnlyAcceptsTheKnownTrayProtocolAndProcess(uint expected, uint actual, string windowClass, bool supported) =>
        Assert.Equal(supported, DockWeChatTrayActivation.IsSupportedTrayWindow(expected, actual, windowClass));

    [Theory]
    [InlineData(100, 200, 0x00C80064u)]
    [InlineData(-20, -10, 0xFFF6FFECu)]
    [InlineData(2800, -100, 0xFF9C0AF0u)]
    public void CallbackPreservesSignedScreenCoordinates(int left, int top, uint packed) =>
        Assert.Equal((nuint)packed, DockWeChatTrayActivation.CallbackCoordinates(new() { Left = left, Top = top, Right = left + 24, Bottom = top + 24 }));

    [Fact]
    public void ReadOnlyLookupRejectsAnUnavailableProcess() =>
        Assert.False(DockWeChatTrayActivation.TryInvoke(0, null, invoke: false));

    [Fact]
    public void VisibleButtonMatchRejectsAdjacentPartialAndEmptyBounds()
    {
        var icon = new DockWeChatTrayActivation.Rect { Left = 100, Top = 200, Right = 148, Bottom = 272 };
        Assert.True(DockWeChatTrayActivation.ContainsIcon(icon, icon));
        Assert.False(DockWeChatTrayActivation.ContainsIcon(new() { Left = 200, Top = 200, Right = 248, Bottom = 272 }, icon));
        Assert.False(DockWeChatTrayActivation.ContainsIcon(new() { Left = 100, Top = 200, Right = 147, Bottom = 272 }, icon));
        Assert.False(DockWeChatTrayActivation.ContainsIcon(icon, default));
    }
}
