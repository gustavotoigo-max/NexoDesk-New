using System.Net;
using NexoDesk.Shared.Security;
using NexoDesk.Shared.Configuration;

namespace NexoDesk.Shared.Tests;

public class PrivateNetworkTests
{
    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.1.2", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("100.100.1.2", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    public void LocalMode_AcceptsOnlyPrivateIPv4(string address, bool expected) =>
        Assert.Equal(expected, PrivateNetwork.IsLocalAddress(IPAddress.Parse(address)));

    [Fact]
    public void LocalMode_RejectsPublicListenAddress()
    {
        var config = new AppConfig { DeviceName = "Teste", DeviceId = "teste", CertificateSha256 = new string('A',64) };
        config.Remote.NetworkMode = NetworkMode.LocalNetwork;
        config.Remote.ListenAddress = "8.8.8.8";
        Assert.Contains(config.Validate(), p => p.Contains("privado"));
        config.Remote.ListenAddress = "192.168.1.2";
        Assert.Empty(config.Validate());
    }
}