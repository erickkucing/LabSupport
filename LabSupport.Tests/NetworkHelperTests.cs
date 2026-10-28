using System.Net;
using Xunit;
using LabSupport.Shared.Services;

namespace LabSupport.Tests;

public class NetworkHelperTests
{
    [Theory]
    [InlineData("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", true)]
    [InlineData("VirtualBox Host-Only Network", "VirtualBox Host-Only Ethernet Adapter", true)]
    [InlineData("VMware Network Adapter VMnet1", "VMware Virtual Ethernet Adapter", true)]
    [InlineData("docker0", "Docker Container Virtual Interface", true)]
    [InlineData("veth8a3b1c", "", true)]
    [InlineData("Local Area Connection 2", "TAP-Windows Adapter V9", true)]
    [InlineData("Ethernet 2", "TAP Virtual Ethernet Adapter", true)]
    [InlineData("wsl-net", "WSL Virtual Adapter", true)]
    [InlineData("wg0", "WireGuard Tunnel", true)]
    [InlineData("Tailscale", "Tailscale Tunnel", true)]
    [InlineData("OpenVPN", "OpenVPN Data Channel Offload", true)]
    [InlineData("Ethernet 1", "Intel(R) Ethernet Connection I219-V", false)]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz", false)]
    [InlineData("Ethernet", "Realtek PCIe 2.5GbE Family Controller", false)]
    public void IsVirtualAdapter_CorrectlyIdentifiesAdapters(string name, string desc, bool expectedVirtual)
    {
        bool isVirtual = NetworkHelper.IsVirtualAdapter(name, desc);
        Assert.Equal(expectedVirtual, isVirtual);
    }

    [Fact]
    public void GetLocalIPv4Addresses_ReturnsAtLeastOneValidIPv4()
    {
        var ips = NetworkHelper.GetLocalIPv4Addresses();

        Assert.NotNull(ips);
        Assert.NotEmpty(ips);
        foreach (var ip in ips)
        {
            Assert.True(IPAddress.TryParse(ip, out var parsed));
            Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, parsed.AddressFamily);
        }
    }

    [Fact]
    public void GetPrimaryLanIPv4Address_ReturnsValidIPv4Address()
    {
        string primaryIp = NetworkHelper.GetPrimaryLanIPv4Address();

        Assert.False(string.IsNullOrWhiteSpace(primaryIp));
        Assert.True(IPAddress.TryParse(primaryIp, out var parsed));
        Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, parsed.AddressFamily);
    }
}
