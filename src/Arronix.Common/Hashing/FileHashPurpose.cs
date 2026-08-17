namespace Arronix.Common.Hashing;

/// <summary>
/// Why a file is being hashed, which is what selects the algorithm.
/// </summary>
/// <remarks>
/// The caller states its intent and the platform chooses the primitive. Naming the algorithm at the call
/// site instead would freeze it into every caller, so replacing it later would mean editing all of them —
/// and it invites a caller to pick a fast hash for a job that needed a cryptographic one.
/// </remarks>
public enum FileHashPurpose
{
    /// <summary>
    /// The digest answers "has this file changed?". A fast, non-cryptographic hash is used. It carries no
    /// guarantee against an adversary who chooses the file contents, so it must not be used to decide
    /// whether a download, an update package or a plugin is authentic.
    /// </summary>
    ChangeDetection = 0,

    /// <summary>
    /// The digest answers "is this file exactly the one that was published?". A cryptographic hash is used,
    /// at a cost of roughly an order of magnitude in throughput.
    /// </summary>
    Integrity = 1,
}
