using System;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Common.Http.Proxy;

public class HttpProxySettings(ProxyType type, string host, int port, string bypassFilter, bool bypassLocalAddress, string username = null, string password = null)
{
    public ProxyType Type { get; private set; } = type;
    public string Host { get; private set; } = host.IsNullOrWhiteSpace() ? "127.0.0.1" : host;
    public int Port { get; private set; } = port;
    public string Username { get; private set; } = username ?? string.Empty;
    public string Password { get; private set; } = password ?? string.Empty;
    public string BypassFilter { get; private set; } = bypassFilter ?? string.Empty;
    public bool BypassLocalAddress { get; private set; } = bypassLocalAddress;

    public string[] BypassListAsArray
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BypassFilter))
            {
                var hostlist = BypassFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                for (var i = 0; i < hostlist.Length; i++)
                {
                    if (hostlist[i].StartsWith("*"))
                    {
                        hostlist[i] = ";" + hostlist[i];
                    }
                }

                return hostlist;
            }

            return Array.Empty<string>();
        }
    }

    public string Key => string.Join("_",
        Type,
        Host,
        Port,
        Username,
        Password,
        BypassFilter,
        BypassLocalAddress);
}
