using System;
using System.Collections.Generic;
using Arronix.Common.Hosting;

namespace Arronix.Common.Tests.Hosting;

/// <summary>
/// A platform to run the hosting facts against, so the Docker, Podman, service and privilege branches are
/// covered without the developer's machine having to be any of those things.
/// </summary>
internal sealed class PlatformFactsStub : IPlatformFacts
{
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public bool IsWindows { get; set; }

    public bool IsLinux { get; set; }

    public bool IsMacOS { get; set; }

    public bool IsFreeBsd { get; set; }

    public string OperatingSystemDescription { get; set; } = "Test platform 1.0";

    public Version OperatingSystemVersion { get; set; } = new(1, 0);

    public bool IsPrivilegedProcess { get; set; }

    public bool IsUserInteractive { get; set; } = true;

    public string? ProcessPath { get; set; } = "/opt/arronix/Arronix.Api";

    public DateTimeOffset? ProcessStartTime { get; set; } = DateTimeOffset.UnixEpoch.AddHours(1);

    public string? EnvironmentVariable(string name)
        => _environment.TryGetValue(name, out var value) ? value : null;

    public bool FileExists(string path) => _files.ContainsKey(path);

    public string? ReadFile(string path) => _files.TryGetValue(path, out var value) ? value : null;

    internal PlatformFactsStub WithEnvironment(string name, string value)
    {
        _environment[name] = value;
        return this;
    }

    internal PlatformFactsStub WithFile(string path, string contents = "")
    {
        _files[path] = contents;
        return this;
    }

    internal static PlatformFactsStub Linux() => new() { IsLinux = true };

    internal static PlatformFactsStub Windows() => new() { IsWindows = true };
}
