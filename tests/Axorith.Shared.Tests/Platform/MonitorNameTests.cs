using System.Runtime.Versioning;
using System.Text;
using Axorith.Shared.Platform.Windows;
using Xunit;

namespace Axorith.Shared.Tests.Platform;

[SupportedOSPlatform("windows")]
public sealed class MonitorNameTests
{
    [Theory]
    [InlineData(54)]
    [InlineData(72)]
    [InlineData(90)]
    [InlineData(108)]
    public void EdidDisplayNameIsReadFromAnyDescriptorWithoutWmi(int offset)
    {
        var edid = CreateEdid();
        edid[offset + 3] = 0xFC;
        Encoding.ASCII.GetBytes("DELL U2723QE\n").CopyTo(edid, offset + 5);
        SetChecksum(edid);
        Assert.Equal("DELL U2723QE", WindowApi.DecodeEdidMonitorName(edid));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("header")]
    [InlineData("checksum")]
    public void InvalidEdidIsRejectedSoTheWindowsDeviceNameCanBeUsed(string damage)
    {
        var edid = CreateEdid();
        if (damage == "short") edid = edid[..127];
        else if (damage == "header") { edid[0] = 1; SetChecksum(edid); }
        else edid[127]++;
        Assert.Null(WindowApi.DecodeEdidMonitorName(edid));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrBlankDisplayNameKeepsManufacturerAndProductFallback(bool blankName)
    {
        var edid = CreateEdid();
        edid[57] = blankName ? (byte)0xFC : (byte)0xFF;
        Encoding.ASCII.GetBytes(blankName ? "   \n" : "serial number").CopyTo(edid, 59);
        SetChecksum(edid);
        Assert.Equal("DEL 409B", WindowApi.DecodeEdidMonitorName(edid));
    }

    private static byte[] CreateEdid()
    {
        var edid = new byte[128];
        new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 }.CopyTo(edid, 0);
        edid[8] = 0x10; edid[9] = 0xAC; // DEL, EISA manufacturer ID.
        edid[10] = 0x9B; edid[11] = 0x40;
        SetChecksum(edid);
        return edid;
    }

    private static void SetChecksum(byte[] edid) =>
        edid[127] = unchecked((byte)-edid.Take(127).Sum(value => (int)value));
}
