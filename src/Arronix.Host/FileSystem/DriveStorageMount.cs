using System.Diagnostics.CodeAnalysis;
using System.IO;
using Arronix.Abstractions.FileSystem;


namespace Arronix.Host.FileSystem;

/// <summary>
/// One mounted volume, as the framework reports it.
/// </summary>
/// <remarks>
/// A thin projection rather than a snapshot: free space is read at the moment it is asked for, because the
/// only reason to ask is to decide whether an import will fit, and a value cached at construction time
/// would be exactly the wrong one.
/// </remarks>
/// <param name="drive">The volume being described.</param>
internal sealed class DriveStorageMount(DriveInfo drive) : IStorageMount
{
    private readonly DriveInfo _drive = drive;

    /// <inheritdoc />
    public string Name => _drive.Name;

    /// <inheritdoc />
    public string RootDirectory => _drive.RootDirectory.FullName;

    /// <inheritdoc />
    public string VolumeName => _drive.Name;

    /// <inheritdoc />
    public string? VolumeLabel => _drive.IsReady ? Safe(() => _drive.VolumeLabel) : null;

    /// <inheritdoc />
    public string DriveFormat => (_drive.IsReady ? Safe(() => _drive.DriveFormat) : null) ?? string.Empty;

    /// <inheritdoc />
    public DriveType DriveType => _drive.DriveType;

    /// <inheritdoc />
    public bool IsReady => _drive.IsReady;

    /// <inheritdoc />
    public long AvailableFreeSpace => _drive.IsReady ? _drive.AvailableFreeSpace : 0L;

    /// <inheritdoc />
    public long TotalFreeSpace => _drive.IsReady ? _drive.TotalFreeSpace : 0L;

    /// <inheritdoc />
    public long TotalSize => _drive.IsReady ? _drive.TotalSize : 0L;

    /// <inheritdoc />
    /// <remarks>
    /// Always absent. Mount options come from the platform-specific probes the shared assembly does not
    /// ship, and reporting a guess would be worse than reporting nothing.
    /// </remarks>
    public StorageMountOptions? MountOptions => null;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Volume metadata is unavailable on a device that becomes unready between the readiness check and the read; the absent value is the correct answer.")]
    private static string? Safe(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
