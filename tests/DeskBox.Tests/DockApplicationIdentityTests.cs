using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class DockApplicationIdentityTests
{
    [Theory]
    [InlineData(@"C:\Apps\Doubao\Application\app\Doubao.exe", @"C:\Apps\Doubao\Application\Doubao.exe")]
    [InlineData(@"C:\Apps\Doubao\Application\Doubao.exe", @"C:\Apps\Doubao\Application\app\Doubao.exe")]
    [InlineData(@"C:\APPS\DOUBAO\Application\APP\Doubao.exe", @"c:\apps\doubao\Application\doubao.exe")]
    [InlineData(@"C:\Apps\Code.exe", @"C:\Apps\Code.exe")]
    [InlineData(@"C:\Apps\Code.exe", @"c:/Apps/./Code.exe")]
    public void RecognizesExactPathsAndImmediateAppLauncher(string executable, string target) =>
        Assert.True(DockApplicationIdentity.Matches(executable, target));

    [Theory]
    [InlineData(@"C:\Other\Application\app\Doubao.exe", @"C:\Apps\Application\Doubao.exe")]
    [InlineData(@"C:\Apps\Application\app\Helper.exe", @"C:\Apps\Application\Doubao.exe")]
    [InlineData(@"C:\Apps\Application\bin\Doubao.exe", @"C:\Apps\Application\Doubao.exe")]
    [InlineData(@"C:\Apps\Application\nested\app\Doubao.exe", @"C:\Apps\Application\Doubao.exe")]
    [InlineData(@"C:\Apps\Application\app\Doubao.exe", @"C:\Apps\Application\Other\Doubao.exe")]
    [InlineData(@"C:\Apps\Application\app\Shortcut.lnk", @"C:\Apps\Application\Shortcut.lnk")]
    [InlineData(@"C:\Apps\Code.exe", @"C:\AnotherInstall\Code.exe")]
    [InlineData(null, @"C:\Apps\Doubao.exe")]
    [InlineData("", @"C:\Apps\Doubao.exe")]
    [InlineData(@"C:\Apps\Doubao.exe", "")]
    [InlineData(@"app\Doubao.exe", "Doubao.exe")]
    public void DoesNotMergeUnrelatedOrUnavailablePaths(string? executable, string target) =>
        Assert.False(DockApplicationIdentity.Matches(executable, target));
}
