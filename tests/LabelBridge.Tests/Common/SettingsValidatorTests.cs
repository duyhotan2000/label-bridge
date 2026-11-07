using LabelBridge.Common;

namespace LabelBridge.Tests.Common;

public class SettingsValidatorTests
{
    [Theory]
    [InlineData("192.168.2.102")]
    [InlineData("printer.local")]
    [InlineData("xp470b")]
    public void ValidateHost_Valid(string host) => Assert.Null(SettingsValidator.ValidateHost(host));

    [Theory]
    [InlineData("")]
    [InlineData("999.1.1.1")]
    [InlineData("a b")]
    [InlineData("-bad.local")]
    public void ValidateHost_Invalid(string host) => Assert.NotNull(SettingsValidator.ValidateHost(host));

    [Theory]
    [InlineData("1")]
    [InlineData("9100")]
    [InlineData("65535")]
    public void ValidatePort_Valid(string port) => Assert.Null(SettingsValidator.ValidatePort(port));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("")]
    public void ValidatePort_Invalid(string port) => Assert.NotNull(SettingsValidator.ValidatePort(port));

    [Fact]
    public void ValidateName_CountsUtf8Bytes()
    {
        var name63 = new string('ố', 21);          // 'ố' is 3 UTF-8 bytes → 63 bytes
        Assert.Null(SettingsValidator.ValidateName(name63));
        Assert.NotNull(SettingsValidator.ValidateName(name63 + "a"));
    }

    [Fact]
    public void ValidateName_RejectsEmptyAndControlChars()
    {
        Assert.NotNull(SettingsValidator.ValidateName(""));
        Assert.NotNull(SettingsValidator.ValidateName("   "));
        Assert.NotNull(SettingsValidator.ValidateName("bad\tname"));
        Assert.Null(SettingsValidator.ValidateName("XP-470B Label (Surface)"));
    }

    [Fact]
    public void Validate_CollectsAllMessages()
    {
        Assert.Equal(3, SettingsValidator.Validate("", "0", "").Count);
        Assert.Empty(SettingsValidator.Validate("192.168.2.102", "9100", "LabelBridge Printer"));
    }
}
