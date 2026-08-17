using System.Globalization;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Everything a declared predicate may examine while one release is being resolved.
/// </summary>
/// <remarks>
/// <para>
/// The reachable subject vocabulary is exactly: <c>tags.*</c> (the host scan's members, then a kind's
/// token-table tags), <c>capture:*</c> (the winning title pattern's named groups), <c>guard:*</c>
/// (declared guard expressions, evaluated lazily and cached per release) and <c>categories</c>. The
/// vocabulary is validated at load; an unresolvable subject refuses the definition rather than
/// evaluating to anything.
/// </para>
/// <para>
/// <see cref="SourceGroup"/> and <see cref="StatedResolution"/> are the two members a declared
/// default row may assume into existence or clear, which is why they are settable here while everything
/// else is read-only.
/// </para>
/// </remarks>
internal sealed class ParsePredicateContext
{
    private const string TagsPrefix = "tags.";
    private const string CapturePrefix = "capture:";
    private const string GuardPrefix = "guard:";
    private const string CategoriesSubject = "categories";

    private readonly string _rawText;
    private readonly string _normalizedText;
    private readonly ScannedReleaseTags _tags;
    private readonly IReadOnlyDictionary<string, string> _captures;
    private readonly CompiledGuardSet _guards;
    private readonly Dictionary<string, bool> _guardResults = new(StringComparer.Ordinal);

    internal ParsePredicateContext(
        string rawText,
        string normalizedText,
        ScannedReleaseTags tags,
        IReadOnlyDictionary<string, string> captures,
        IReadOnlyList<string> categories,
        CompiledGuardSet guards)
    {
        _rawText = rawText;
        _normalizedText = normalizedText;
        _tags = tags;
        _captures = captures;
        Categories = categories;
        _guards = guards;
        SourceGroup = tags.SourceGroup;
        StatedResolution = tags.StatedResolution;
    }

    /// <summary>Gets or sets the effective source group, after any default rows applied.</summary>
    internal string? SourceGroup { get; set; }

    /// <summary>Gets or sets the effective stated resolution, after any default rows applied.</summary>
    internal int StatedResolution { get; set; }

    /// <summary>Gets the release categories the caller supplied. Empty when it supplied none.</summary>
    internal IReadOnlyList<string> Categories { get; }

    /// <summary>Determines whether a subject names a guard.</summary>
    /// <param name="subject">The subject path.</param>
    /// <returns>Whether it is a <c>guard:</c> path.</returns>
    internal static bool IsGuardSubject(string subject) =>
        subject.StartsWith(GuardPrefix, StringComparison.Ordinal);

    /// <summary>Determines whether a subject names the category list.</summary>
    /// <param name="subject">The subject path.</param>
    /// <returns>Whether it is the <c>categories</c> subject.</returns>
    internal static bool IsCategoriesSubject(string subject) =>
        string.Equals(subject, CategoriesSubject, StringComparison.Ordinal);

    /// <summary>Determines whether a subject is spelled in the reachable vocabulary at all.</summary>
    /// <param name="subject">The subject path.</param>
    /// <returns>Whether the spelling is reachable.</returns>
    internal static bool IsWellFormedSubject(string subject) =>
        IsCategoriesSubject(subject)
        || IsGuardSubject(subject)
        || subject.StartsWith(TagsPrefix, StringComparison.Ordinal)
        || subject.StartsWith(CapturePrefix, StringComparison.Ordinal);

    /// <summary>Resolves a scalar subject to its value.</summary>
    /// <param name="subject">The subject path.</param>
    /// <param name="value">The value, when present.</param>
    /// <returns>Whether the subject has a value at all.</returns>
    internal bool TryResolve(string subject, out string? value)
    {
        value = null;

        if (subject.StartsWith(CapturePrefix, StringComparison.Ordinal))
        {
            var group = subject[CapturePrefix.Length..];

            if (_captures.TryGetValue(group, out var captured) && captured.Length > 0)
            {
                value = captured;
                return true;
            }

            return false;
        }

        if (!subject.StartsWith(TagsPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var tag = subject[TagsPrefix.Length..];

        switch (tag)
        {
            case "SourceGroup":
                value = SourceGroup;
                return value is not null;
            case "StatedResolution":
                if (StatedResolution == 0)
                {
                    return false;
                }

                value = StatedResolution.ToString(CultureInfo.InvariantCulture);
                return true;
            case "IsRemux":
                // A boolean tag is absent when false: "present" and "true" are the same statement.
                value = _tags.IsRemux ? "true" : null;
                return _tags.IsRemux;
            case "VideoCodec":
                value = _tags.VideoCodec;
                return value is not null;
            case "AudioCodec":
                value = _tags.AudioCodec;
                return value is not null;
            case "ReleaseGroup":
                value = _tags.ReleaseGroup;
                return value is not null;
            case "RevisionVersion":
#pragma warning disable ARX0013 // The revision axis is an experimental shape contract until 1.0.
                value = _tags.Revision.Version.ToString(CultureInfo.InvariantCulture);
                return true;
            case "RevisionReal":
                value = _tags.Revision.Real.ToString(CultureInfo.InvariantCulture);
                return true;
            case "RevisionIsRepack":
                value = _tags.Revision.IsRepack ? "true" : null;
                return _tags.Revision.IsRepack;
#pragma warning restore ARX0013
            default:
                if (_tags.Extra.TryGetValue(tag, out var extra) && extra.Length > 0)
                {
                    value = extra;
                    return true;
                }

                return false;
        }
    }

    /// <summary>Evaluates a guard, caching the answer for the rest of this release.</summary>
    /// <param name="subject">The <c>guard:</c> subject path.</param>
    /// <returns>Whether the guard matches.</returns>
    internal bool GuardMatches(string subject)
    {
        var guardId = subject[GuardPrefix.Length..];

        if (_guardResults.TryGetValue(guardId, out var cached))
        {
            return cached;
        }

        var result = _guards.Matches(guardId, _rawText, _normalizedText);
        _guardResults[guardId] = result;
        return result;
    }
}
