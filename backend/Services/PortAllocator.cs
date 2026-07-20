using System.Net;
using System.Net.Sockets;
using DockerVm.Data;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Services;

/// <summary>
/// 在配置的端口范围内随机分配一个未被占用、未被分配过的端口。
/// </summary>
public class PortAllocator
{
    private readonly AppDbContext _db;
    private readonly Random _random = new();
    private readonly int _min;
    private readonly int _max;

    public PortAllocator(AppDbContext db, int min, int max)
    {
        _db = db;
        _min = Math.Min(min, max);
        _max = Math.Max(min, max);
    }

    /// <summary>分配一个可用端口,最多尝试 50 次。</summary>
    public async Task<int> AllocateAsync(CancellationToken ct = default)
    {
        var used = await _db.Containers
            .Select(c => (int?)c.HostPort)
            .ToListAsync(ct);

        for (var i = 0; i < 50; i++)
        {
            var port = _random.Next(_min, _max + 1);
            if (used.Contains(port))
            {
                continue;
            }

            if (!IsFree(port))
            {
                continue;
            }

            return port;
        }

        throw new InvalidOperationException($"在 [{_min}, {_max}] 范围内找不到可用端口");
    }

    private static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
