namespace RouterTray.Tests;

public sealed class AppInstallationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(122)]
    public void ProbeResultHasPackageIdentity_AcceptsPackagedResults(int result)
    {
        Assert.True(AppInstallation.ProbeResultHasPackageIdentity(result));
    }

    [Theory]
    [InlineData(15700)]
    [InlineData(5)]
    public void ProbeResultHasPackageIdentity_RejectsUnpackagedOrFailedResults(int result)
    {
        Assert.False(AppInstallation.ProbeResultHasPackageIdentity(result));
    }
}
