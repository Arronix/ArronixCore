using System.IO;

namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// A mounted volume, as far as the host can see it.
/// </summary>
/// <remarks>
/// Returned by the file system contract and consumed by free-space and read-only health checks that
/// extensions contribute, which is why it crosses the boundary rather than staying host-side.
/// </remarks>
public interface IStorageMount
{
    /// <summary>
    /// Gets the mount's name as the platform reports it.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the directory the volume is mounted at.
    /// </summary>
    string RootDirectory { get; }

    /// <summary>
    /// Gets the volume's device or share name.
    /// </summary>
    string VolumeName { get; }

    /// <summary>
    /// Gets the volume label, or <see langword="null"/> when the platform does not expose one.
    /// </summary>
    string? VolumeLabel { get; }

    /// <summary>
    /// Gets the file system format, for example <c>ext4</c> or <c>NTFS</c>.
    /// </summary>
    string DriveFormat { get; }

    /// <summary>
    /// Gets the kind of device backing the mount.
    /// </summary>
    DriveType DriveType { get; }

    /// <summary>
    /// Gets a value indicating whether the mount is currently available. Reading the size properties of
    /// a mount that is not ready is not meaningful.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Gets the free space available to the current user, in bytes. This is the figure to check before
    /// writing, because quotas can make it smaller than <see cref="TotalFreeSpace"/>.
    /// </summary>
    long AvailableFreeSpace { get; }

    /// <summary>
    /// Gets the total free space on the volume, in bytes, ignoring quotas.
    /// </summary>
    long TotalFreeSpace { get; }

    /// <summary>
    /// Gets the volume's total capacity, in bytes.
    /// </summary>
    long TotalSize { get; }

    /// <summary>
    /// Gets the options the volume was mounted with, or <see langword="null"/> when no platform pack
    /// could read them.
    /// </summary>
    StorageMountOptions? MountOptions { get; }
}
