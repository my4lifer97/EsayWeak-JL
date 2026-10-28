using System.Net;
using System.Net.Sockets;

namespace BarberSaas.Api.Services;

// The client IP behind Railway's proxy (LoginThrottle, activity log). RemoteIpAddress is the proxy's.
// 2026-10-12 X-Real-IP held the client; by 2026-10-28 the activity log showed Railway addresses again,
// so don't trust one header: take the first public address from X-Forwarded-For (client first, then
// proxies), then X-Real-IP. Private, loopback, link-local and CGNAT (100.64/10, Railway's internal
// network) addresses are skipped.
public static class ClientIp
{
    public static IPAddress? Resolve(IHeaderDictionary headers)
    {
        var candidates = headers["X-Forwarded-For"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(headers["X-Real-IP"].ToString().Trim());
        IPAddress? fallback = null;
        foreach (var value in candidates)
        {
            if (!IPAddress.TryParse(value, out var ip)) continue;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IsPublic(ip)) return ip;
            fallback ??= ip;
        }
        return fallback;
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal);
        var b = ip.GetAddressBytes();
        return !(b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || b[0] == 0);
    }
}
