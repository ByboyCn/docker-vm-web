using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DockerVm.Services;

/// <summary>
/// 探测返回给用户的宿主机 IP。
/// 优先级:配置 > 非环回/非 docker 网桥的 IPv4。
/// </summary>
public static class HostIpDetector
{
    public static string Detect(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            // 跳过 loopback / docker 网桥 / 虚拟网络
            var name = ni.Name.ToLowerInvariant();
            if (name.Contains("lo") || name.Contains("docker") || name.Contains("br-") || name.Contains("veth"))
            {
                continue;
            }

            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork
                    && !addr.Address.ToString().StartsWith("127."))
                {
                    return addr.Address.ToString();
                }
            }
        }

        return "127.0.0.1";
    }
}
