namespace RouterTray.Tests;

public sealed class MacAddressInspectorTests
{
    [Theory]
    [InlineData("02:11:22:33:44:55", "02:11:22:33:44:55")]
    [InlineData("0A-1b-2C-3d-4E-5f", "0A:1B:2C:3D:4E:5F")]
    [InlineData("0011.2233.4455", "00:11:22:33:44:55")]
    public void TryNormalize_FormatsValidUnicastAddress(string value, string expected)
    {
        Assert.True(MacAddressInspector.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00:00:00:00:00:00")]
    [InlineData("FF:FF:FF:FF:FF:FF")]
    [InlineData("01:11:22:33:44:55")]
    [InlineData("GG:11:22:33:44:55")]
    public void TryNormalize_RejectsInvalidOrNonUnicastAddress(string value)
    {
        Assert.False(MacAddressInspector.TryNormalize(value, out _));
    }

    [Theory]
    [InlineData("02:11:22:33:44:55", true)]
    [InlineData("06:11:22:33:44:55", true)]
    [InlineData("00:11:22:33:44:55", false)]
    [InlineData("DC:11:22:33:44:55", false)]
    public void IsLocallyAdministered_RecognizesPrivacyAddressFormat(
        string value,
        bool expected)
    {
        Assert.Equal(expected, MacAddressInspector.IsLocallyAdministered(value));
    }
}
