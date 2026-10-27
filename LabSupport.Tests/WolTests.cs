using System;
using Xunit;
using LabSupport.Shared.Protocol;

namespace LabSupport.Tests;

public class WolTests
{
    [Theory]
    [InlineData("00:11:22:33:44:55")]
    [InlineData("00-11-22-33-44-55")]
    [InlineData("001122334455")]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    public void CreateMagicPacket_ShouldGenerate102BytesWithCorrectHeaderAndPayload(string mac)
    {
        byte[] packet = WolHelper.CreateMagicPacket(mac);

        Assert.Equal(102, packet.Length);

        // First 6 bytes must be 0xFF
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(0xFF, packet[i]);
        }

        // Following 16 blocks must repeat the MAC bytes
        byte[] expectedMac = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };
        if (mac.StartsWith("aa", StringComparison.OrdinalIgnoreCase))
        {
            expectedMac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        }

        for (int rep = 0; rep < 16; rep++)
        {
            for (int b = 0; b < 6; b++)
            {
                Assert.Equal(expectedMac[b], packet[6 + (rep * 6) + b]);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-mac")]
    [InlineData("00:11:22:33")]
    public void CreateMagicPacket_ShouldThrowOnInvalidMac(string invalidMac)
    {
        Assert.ThrowsAny<ArgumentException>(() => WolHelper.CreateMagicPacket(invalidMac));
    }
}
