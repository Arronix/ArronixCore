
namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// A file system path expressed in a named platform's grammar, which may not be the grammar of the
/// machine currently executing.
/// </summary>
/// <remarks>
/// <para>
/// The framework's path APIs always reason in the running machine's grammar. That is the wrong model
/// for remote path mapping, where the host has to reason about a path on another machine: a download
/// client running in a container reports a path in its own namespace, and the importer has to translate
/// it into one the host can open. Doing that with the framework's helpers silently produces nonsense
/// whenever the two platforms disagree.
/// </para>
/// <para>
/// Instances are normalized on construction: separators are converted to the grammar's separator and
/// repeated separators in a Unix path are collapsed. A trailing separator is preserved, because it is
/// how a caller says "this is a directory" about a path that does not exist locally and therefore
/// cannot be probed.
/// </para>
/// <para>
/// Equality ignores a trailing separator and, for the Windows grammar, ignores case. The hash is
/// computed case-insensitively in every grammar, which keeps it consistent with an equality that is
/// case-insensitive only sometimes.
/// </para>
/// </remarks>
public readonly struct PlatformPath : IEquatable<PlatformPath>
{
    private const char WindowsSeparator = '\\';
    private const char UnixSeparator = '/';

    private readonly string? _path;
    private readonly PlatformPathKind _kind;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformPath"/> struct, inferring the grammar from
    /// the text.
    /// </summary>
    /// <param name="path">The path text, which may be <see langword="null"/>.</param>
    public PlatformPath(string? path)
        : this(path, path is null ? PlatformPathKind.Unknown : Detect(path))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformPath"/> struct in a stated grammar.
    /// </summary>
    /// <param name="path">The path text, which may be <see langword="null"/>.</param>
    /// <param name="kind">The grammar to interpret the text in.</param>
    public PlatformPath(string? path, PlatformPathKind kind)
    {
        _kind = kind;
        _path = path is null ? string.Empty : Normalize(path, kind);
    }

    /// <summary>
    /// Gets the empty path, whose grammar is <see cref="PlatformPathKind.Unknown"/>.
    /// </summary>
    public static PlatformPath Empty => default;

    /// <summary>
    /// Gets the grammar this path is written in.
    /// </summary>
    public PlatformPathKind Kind => _kind;

    /// <summary>
    /// Gets the normalized path text. Never <see langword="null"/>.
    /// </summary>
    public string FullPath => _path ?? string.Empty;

    /// <summary>
    /// Gets the path text with any trailing separator removed, except where the separator is part of
    /// the root.
    /// </summary>
    public string PathWithoutTrailingSeparator => TrimTrailingSeparator(FullPath, _kind);

    /// <summary>
    /// Gets a value indicating whether the path is empty or whitespace.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(_path);

    /// <summary>
    /// Gets a value indicating whether the path is absolute in its own grammar.
    /// </summary>
    public bool IsRooted => RootLength(FullPath, _kind) > 0;

    /// <summary>
    /// Gets the parent directory, with a trailing separator, or <see cref="Empty"/> when the path is a
    /// root or has no parent.
    /// </summary>
    public PlatformPath Directory
    {
        get
        {
            var trimmed = PathWithoutTrailingSeparator;
            var rootLength = RootLength(trimmed, _kind);

            if (trimmed.Length <= rootLength)
            {
                return Empty;
            }

            var index = trimmed.LastIndexOf(SeparatorFor(_kind));

            if (index < 0 || index < rootLength)
            {
                return rootLength == 0 ? Empty : new PlatformPath(trimmed[..rootLength], _kind);
            }

            return new PlatformPath(trimmed[..index], _kind).AsDirectory();
        }
    }

    /// <summary>
    /// Gets the last segment of the path — the file name, or the directory name for a path written with
    /// a trailing separator. Empty for a root or an empty path.
    /// </summary>
    public string Name
    {
        get
        {
            var trimmed = PathWithoutTrailingSeparator;
            var rootLength = RootLength(trimmed, _kind);

            if (trimmed.Length <= rootLength)
            {
                return trimmed;
            }

            var index = trimmed.LastIndexOf(SeparatorFor(_kind));

            return index < 0 ? trimmed : trimmed[(index + 1)..];
        }
    }

    /// <summary>
    /// Gets the file name, or <see langword="null"/> when the path was written as a directory — that
    /// is, when it ends in a separator, is a root, or is empty.
    /// </summary>
    public string? FileName
    {
        get
        {
            var full = FullPath;

            if (full.Length == 0 || full[^1] == SeparatorFor(_kind))
            {
                return null;
            }

            var name = Name;

            return name.Length == 0 ? null : name;
        }
    }

    /// <summary>
    /// Determines whether two paths are equal.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns><see langword="true"/> when the paths are equal.</returns>
    public static bool operator ==(PlatformPath left, PlatformPath right) => left.Equals(right);

    /// <summary>
    /// Determines whether two paths differ.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns><see langword="true"/> when the paths differ.</returns>
    public static bool operator !=(PlatformPath left, PlatformPath right) => !left.Equals(right);

    /// <summary>
    /// Appends one path to another.
    /// </summary>
    /// <param name="left">The base path.</param>
    /// <param name="right">The path to append.</param>
    /// <returns>The combined path.</returns>
    public static PlatformPath operator +(PlatformPath left, PlatformPath right) => left.Combine(right);

    /// <summary>
    /// Appends one path to another. The named alternative to <c>operator +</c>.
    /// </summary>
    /// <param name="left">The base path.</param>
    /// <param name="right">The path to append.</param>
    /// <returns>The combined path.</returns>
    public static PlatformPath Add(PlatformPath left, PlatformPath right) => left.Combine(right);

    /// <summary>
    /// Returns this path written as a directory, that is, with a trailing separator.
    /// </summary>
    /// <returns>The path with a trailing separator, or this path when it is empty or of unknown grammar.</returns>
    public PlatformPath AsDirectory()
    {
        if (IsEmpty || _kind == PlatformPathKind.Unknown)
        {
            return this;
        }

        var separator = SeparatorFor(_kind);
        var full = FullPath;

        return full[^1] == separator ? this : new PlatformPath(full + separator, _kind);
    }

    /// <summary>
    /// Appends a path to this one.
    /// </summary>
    /// <param name="other">The path to append. A rooted path replaces this one entirely.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentException">
    /// The two paths are written in different, known grammars. Combining a Windows path with a Unix one
    /// has no correct answer, so it is rejected rather than guessed at.
    /// </exception>
    public PlatformPath Combine(PlatformPath other)
    {
        if (_kind != PlatformPathKind.Unknown
            && other._kind != PlatformPathKind.Unknown
            && _kind != other._kind)
        {
            throw new ArgumentException(
                $"Cannot combine a {_kind} path with a {other._kind} path ('{FullPath}' and '{other.FullPath}').",
                nameof(other));
        }

        if (other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty || other.IsRooted)
        {
            return other;
        }

        var kind = _kind != PlatformPathKind.Unknown ? _kind : other._kind;
        var separator = SeparatorFor(kind);
        var left = TrimTrailingSeparator(FullPath, kind);

        return new PlatformPath($"{left}{separator}{other.FullPath}", kind);
    }

    /// <summary>
    /// Determines whether another path is this path or lies beneath it.
    /// </summary>
    /// <param name="other">The candidate descendant.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="other"/> is inside this path. Always
    /// <see langword="false"/> unless both paths are rooted, because containment between relative paths
    /// is not decidable.
    /// </returns>
    /// <remarks>
    /// Comparison is segment by segment, so <c>/media/library</c> does not contain
    /// <c>/media/library2</c>.
    /// </remarks>
    public bool Contains(PlatformPath other)
    {
        if (!IsRooted || !other.IsRooted)
        {
            return false;
        }

        var left = Segments(PathWithoutTrailingSeparator, _kind);
        var right = Segments(other.PathWithoutTrailingSeparator, other._kind);

        if (right.Length < left.Length)
        {
            return false;
        }

        var comparison = ComparisonFor(_kind, other._kind);

        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], comparison))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether this path equals another.
    /// </summary>
    /// <param name="other">The path to compare with.</param>
    /// <returns><see langword="true"/> when the paths are equal.</returns>
    public bool Equals(PlatformPath other) => string.Equals(
        PathWithoutTrailingSeparator,
        other.PathWithoutTrailingSeparator,
        ComparisonFor(_kind, other._kind));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PlatformPath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(PathWithoutTrailingSeparator);

    /// <inheritdoc />
    public override string ToString() => FullPath;

    private static PlatformPathKind Detect(string path)
    {
        if (path.StartsWith(UnixSeparator))
        {
            return PlatformPathKind.Unix;
        }

        if (HasDriveLetter(path) || path.Contains(WindowsSeparator))
        {
            return PlatformPathKind.Windows;
        }

        return path.Contains(UnixSeparator) ? PlatformPathKind.Unix : PlatformPathKind.Unknown;
    }

    private static bool HasDriveLetter(ReadOnlySpan<char> path) =>
        path.Length >= 2
        && char.IsAsciiLetter(path[0])
        && path[1] == ':'
        && (path.Length == 2 || path[2] == WindowsSeparator || path[2] == UnixSeparator);

    private static char SeparatorFor(PlatformPathKind kind) =>
        kind == PlatformPathKind.Windows ? WindowsSeparator : UnixSeparator;

    private static StringComparison ComparisonFor(PlatformPathKind left, PlatformPathKind right) =>
        left == PlatformPathKind.Windows || right == PlatformPathKind.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Normalize(string path, PlatformPathKind kind)
    {
        switch (kind)
        {
            case PlatformPathKind.Windows:
                return path.Replace(UnixSeparator, WindowsSeparator);

            case PlatformPathKind.Unix:
                var unix = path.Replace(WindowsSeparator, UnixSeparator);

                while (unix.Contains("//", StringComparison.Ordinal))
                {
                    unix = unix.Replace("//", "/", StringComparison.Ordinal);
                }

                return unix;

            default:
                return path;
        }
    }

    private static string TrimTrailingSeparator(string path, PlatformPathKind kind)
    {
        if (kind == PlatformPathKind.Unknown || path.Length == 0)
        {
            return path;
        }

        var separator = SeparatorFor(kind);
        var rootLength = RootLength(path, kind);
        var end = path.Length;

        while (end > rootLength && path[end - 1] == separator)
        {
            end--;
        }

        return end == path.Length ? path : path[..end];
    }

    private static int RootLength(string path, PlatformPathKind kind)
    {
        if (path.Length == 0)
        {
            return 0;
        }

        if (kind == PlatformPathKind.Unix)
        {
            return path[0] == UnixSeparator ? 1 : 0;
        }

        if (kind != PlatformPathKind.Windows)
        {
            return 0;
        }

        if (HasDriveLetter(path))
        {
            return Math.Min(3, path.Length);
        }

        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return ShareRootLength(path, 8);
        }

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return HasDriveLetter(path.AsSpan(4)) ? Math.Min(7, path.Length) : 4;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal) ? ShareRootLength(path, 2) : 0;
    }

    private static int ShareRootLength(string path, int start)
    {
        if (start >= path.Length)
        {
            return path.Length;
        }

        var index = path.IndexOf(WindowsSeparator, start);

        if (index < 0)
        {
            return path.Length;
        }

        index = path.IndexOf(WindowsSeparator, index + 1);

        return index < 0 ? path.Length : index;
    }

    private static string[] Segments(string path, PlatformPathKind kind) =>
        path.Split(SeparatorFor(kind), StringSplitOptions.RemoveEmptyEntries);
}
