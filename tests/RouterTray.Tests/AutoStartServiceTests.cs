namespace RouterTray.Tests;

public sealed class AutoStartServiceTests
{
    [Fact]
    public void EntryTargetsExecutable_MatchesOwnedEntryIgnoringPathCase()
    {
        Assert.True(AutoStartService.EntryTargetsExecutable(
            "\"C:\\Apps\\RouterTray\\RouterTray.exe\"",
            "c:\\apps\\routertray\\routertray.exe"));
    }

    [Fact]
    public void EntryTargetsExecutable_RejectsEntryOwnedByAnotherInstallation()
    {
        Assert.False(AutoStartService.EntryTargetsExecutable(
            "\"C:\\Apps\\RouterTray-old\\RouterTray.exe\"",
            "C:\\Apps\\RouterTray\\RouterTray.exe"));
    }
}
