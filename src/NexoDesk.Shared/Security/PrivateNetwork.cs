using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NexoDesk.Shared.Configuration;

namespace NexoDesk.Shared.Security;

public static class PrivateNetwork
{
    public static bool IsLocalAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    public static IReadOnlyList<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
            .Where(IsLocalAddress).Distinct().OrderBy(a => a.ToString(), StringComparer.Ordinal).ToList();

    public static IPAddress Resolve(RemoteSettings settings)
    {
        if (settings.NetworkMode == NetworkMode.Tailscale)
            return TailscaleInterfaces.ResolveListenAddress(settings.ListenAddress, false);
        IReadOnlyList<IPAddress> active = LocalAddresses();
        if (settings.ListenAddress == "auto")
        {
            if (active.Count == 1) return active[0];
            throw new ConfigException(active.Count == 0
                ? "Nenhum endereço de rede local encontrado."
                : "Há mais de um endereço de rede local. Selecione o IP deste computador.");
        }
        if (!IPAddress.TryParse(settings.ListenAddress, out IPAddress? selected) || !active.Contains(selected))
            throw new ConfigException("Selecione um IP privado ativo deste computador.");
        return selected;
    }
}