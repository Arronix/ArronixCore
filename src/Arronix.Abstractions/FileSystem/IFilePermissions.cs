using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// Reads and applies platform file permissions.
/// </summary>
/// <remarks>
/// <para>
/// Permission models do not generalize — one platform has a numeric mode and an owning group, another
/// has access control entries — so implementations are supplied by platform packs rather than written
/// once. Extracting these members from the file system contract is precisely what allows that contract
/// to stay narrow enough to hand to an extension.
/// </para>
/// <para>
/// There is deliberately no "grant everyone access" member. It existed in the code this replaces, and a
/// permission API whose easiest call makes a file world-writable is not one worth carrying forward.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.FileSystem, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IFilePermissions
{
    /// <summary>
    /// Gets a value indicating whether this implementation understands the current platform's
    /// permission model.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Applies a permission mask and owning group to a file or folder.
    /// </summary>
    /// <param name="path">The file or folder to change.</param>
    /// <param name="mask">The permission mask, in the platform's own notation.</param>
    /// <param name="group">The owning group, or <see langword="null"/> to leave it unchanged.</param>
    void SetPermissions(string path, string mask, string? group);

    /// <summary>
    /// Copies the permissions of one file or folder onto another.
    /// </summary>
    /// <param name="sourcePath">The file or folder to read permissions from.</param>
    /// <param name="targetPath">The file or folder to apply them to.</param>
    void CopyPermissions(string sourcePath, string targetPath);

    /// <summary>
    /// Resets a file or folder to inherit its parent's permissions, discarding any it carries itself.
    /// </summary>
    /// <param name="path">The file or folder to change.</param>
    void InheritPermissions(string path);

    /// <summary>
    /// Determines whether a mask is well-formed for the current platform, so that a configuration error
    /// can be reported at validation time rather than at the moment a file is written.
    /// </summary>
    /// <param name="mask">The permission mask to check.</param>
    /// <returns><see langword="true"/> when the mask is valid.</returns>
    bool IsValidPermissionMask(string mask);
}
