using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// The mount options a storage mount was mounted with.
/// </summary>
/// <param name="IsReadOnly">
/// Whether the mount rejects writes. Supplied as a decided boolean by the platform pack that read the
/// mount table, rather than being inferred by the caller from a raw option name: the spelling of that
/// option is one platform's mount-table syntax and does not belong in cross-platform code.
/// </param>
[Experimental(ExperimentalContracts.FileSystem, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record StorageMountOptions(bool IsReadOnly)
{
    /// <summary>
    /// Gets the remaining options exactly as the platform reported them, for diagnostics. Consumers
    /// should not branch on these; anything worth branching on gets a decided property of its own.
    /// </summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = ReadOnlyDictionary<string, string>.Empty;
}
