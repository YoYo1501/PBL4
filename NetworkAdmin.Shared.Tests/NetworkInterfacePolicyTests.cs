using NetworkAdmin.Shared.Policies;
using Xunit;

namespace NetworkAdmin.Shared.Tests;

public sealed class NetworkInterfacePolicyTests
{
    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.1")]
    [InlineData("223.255.255.254")]
    public void HasUsableIpv4_accepts_usable_unicast_ipv4(string address)
    {
        Assert.True(NetworkInterfacePolicy.HasUsableIpv4(address));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.10.20")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    public void HasUsableIpv4_rejects_non_usable_addresses(string address)
    {
        Assert.False(NetworkInterfacePolicy.HasUsableIpv4(address));
    }

    [Theory]
    [InlineData("VMware Network Adapter VMnet8", "VMware Virtual Ethernet Adapter")]
    [InlineData("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData("Bluetooth Network Connection", "Bluetooth Device")]
    [InlineData("Pseudo Interface", "Network pseudo adapter")]
    [InlineData("Ethernet", "VirtualBox Host-Only Ethernet Adapter")]
    public void IsVirtual_detects_common_virtual_or_non_lan_adapters(string name, string description)
    {
        Assert.True(NetworkInterfacePolicy.IsVirtual(name, description));
    }

    [Fact]
    public void IsVirtual_keeps_real_intel_wifi_adapter_real()
    {
        Assert.False(NetworkInterfacePolicy.IsVirtual("Wi-Fi", "Intel(R) Wi-Fi 6 AX201"));
    }
}
