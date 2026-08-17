using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Arronix.Architecture.Tests.Repository;

/// <summary>
/// One thing declared in source, and where it was declared.
/// </summary>
/// <param name="Name">The declared identifier.</param>
/// <param name="Kind">What was declared, as written.</param>
/// <param name="File">The repository-relative file it was declared in.</param>
/// <param name="Line">The one-based line number.</param>
internal readonly record struct SourceDeclaration(string Name, string Kind, string File, int Line)
{
    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Kind}) at {File}:{Line}";
}

/// <summary>
/// Reads type declarations and prose out of the working tree.
/// </summary>
/// <remarks>
/// <para>
/// A deliberately small reader rather than a compiler front end. The rules it feeds are about names and
/// words, and a full syntactic model would make the fixture depend on the very projects it is policing.
/// The cost of the small reader is that it must be conservative in one specific direction: it must never
/// invent a declaration that is not there, because a false positive in a governance rule is a broken
/// build for no reason.
/// </para>
/// <para>
/// Comment and preprocessor lines are dropped before a declaration is looked for, so the word "class" in
/// a sentence cannot be read as one. Prose is scanned separately, with comments kept, because one rule -
/// the interface-vocabulary rule - is explicitly about what the documentation says as well as what the
/// code declares.
/// </para>
/// </remarks>
internal static partial class SourceScanner
{
    private static readonly char[] IdentifierSeparators = ['_', '.', '-', '`'];

    /// <summary>
    /// Lists every type declared in a project's C# source.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <returns>The declarations, in file order.</returns>
    public static IReadOnlyList<SourceDeclaration> DeclaredTypes(string projectName)
    {
        var declarations = new List<SourceDeclaration>();

        foreach (var path in RepositoryLayout.Files(projectName, "*.cs"))
        {
            declarations.AddRange(DeclaredTypesIn(path));
        }

        // A markup component is a type whose name is its file name, so the file name is the declaration.
        foreach (var path in RepositoryLayout.Files(projectName, "*.razor"))
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (name.Length > 0 && name[0] != '_')
            {
                declarations.Add(new SourceDeclaration(name, "component", RepositoryLayout.Relative(path), 1));
            }
        }

        return declarations;
    }

    /// <summary>
    /// Lists every line of a project's source, comments included.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <param name="searchPatterns">The file patterns to read. Defaults to C# source alone.</param>
    /// <returns>The lines, paired with where they came from.</returns>
    public static IEnumerable<(string File, int Line, string Text)> Lines(
        string projectName,
        params string[] searchPatterns)
    {
        var patterns = searchPatterns.Length > 0 ? searchPatterns : ["*.cs"];

        foreach (var pattern in patterns)
        {
            foreach (var path in RepositoryLayout.Files(projectName, pattern))
            {
                var relative = RepositoryLayout.Relative(path);
                var lines = File.ReadAllLines(path);

                for (var index = 0; index < lines.Length; index++)
                {
                    yield return (relative, index + 1, lines[index]);
                }
            }
        }
    }

    /// <summary>
    /// Lists every line of a project's source with comments removed, blank results dropped.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <param name="searchPatterns">The file patterns to read. Defaults to C# source alone.</param>
    /// <returns>The surviving code text, paired with where it came from.</returns>
    /// <remarks>
    /// The difference from <see cref="Lines"/> is that prose cannot trip a rule. A rule about what the code
    /// <i>does</i> - an attribute that is applied, a keyword that is used - must not fire on a comment
    /// explaining why that thing is not done, which is precisely the comment a corrected decision leaves
    /// behind.
    /// </remarks>
    public static IEnumerable<(string File, int Line, string Text)> CodeLines(
        string projectName,
        params string[] searchPatterns)
    {
        var patterns = searchPatterns.Length > 0 ? searchPatterns : ["*.cs"];

        foreach (var pattern in patterns)
        {
            foreach (var path in RepositoryLayout.Files(projectName, pattern))
            {
                var relative = RepositoryLayout.Relative(path);
                var lines = File.ReadAllLines(path);
                var inBlockComment = false;

                for (var index = 0; index < lines.Length; index++)
                {
                    var code = StripComments(lines[index], ref inBlockComment);

                    if (code.Length > 0)
                    {
                        yield return (relative, index + 1, code);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Lists every identifier-shaped token in a project's source, with comments removed.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <param name="searchPatterns">The file patterns to read. Defaults to C# source alone.</param>
    /// <returns>The tokens, paired with where they came from.</returns>
    /// <remarks>
    /// <para>
    /// Comments are dropped, so this reads what the code <i>calls</i> things rather than what its prose
    /// says about them. String literal contents survive, and that is intended: a wire value and a manifest
    /// key are names the platform owns just as much as a property is.
    /// </para>
    /// <para>
    /// This is a reader, not a parser. It cannot tell a declaration from a use, which is exactly right for
    /// a rule about vocabulary - a name spelled one way where it is declared and another way where it is
    /// called is two problems, not one.
    /// </para>
    /// </remarks>
    public static IEnumerable<(string File, int Line, string Token)> CodeIdentifiers(
        string projectName,
        params string[] searchPatterns)
    {
        var patterns = searchPatterns.Length > 0 ? searchPatterns : ["*.cs"];

        foreach (var pattern in patterns)
        {
            foreach (var path in RepositoryLayout.Files(projectName, pattern))
            {
                var relative = RepositoryLayout.Relative(path);
                var lines = File.ReadAllLines(path);
                var inBlockComment = false;

                for (var index = 0; index < lines.Length; index++)
                {
                    var code = StripComments(lines[index], ref inBlockComment);

                    if (code.Length == 0)
                    {
                        continue;
                    }

                    foreach (Match match in IdentifierPattern().Matches(code))
                    {
                        yield return (relative, index + 1, match.Value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Splits an identifier into the words a reader sees in it.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <returns>The words.</returns>
    /// <remarks>
    /// Word splitting rather than substring matching, and the difference is the whole point. A rule that
    /// forbids the noun "author" must reject <c>AuthorName</c> and accept <c>Authorization</c>; a rule
    /// that forbids "track" must reject <c>TrackFile</c> and accept <c>Tracking</c>. Substring matching
    /// gets both of those wrong, and a governance rule that cries wolf is a governance rule that gets
    /// suppressed.
    /// </remarks>
    public static IReadOnlyList<string> Words(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return [];
        }

        var words = new List<string>();

        foreach (var chunk in identifier.Split(IdentifierSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            SplitCasedChunk(chunk, words);
        }

        return words;
    }

    private static void SplitCasedChunk(string chunk, List<string> words)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < chunk.Length; index++)
        {
            var current = chunk[index];

            var startsWord =
                builder.Length > 0
                && char.IsUpper(current)
                && (!char.IsUpper(chunk[index - 1])
                    || (index + 1 < chunk.Length && char.IsLower(chunk[index + 1])));

            if (startsWord)
            {
                words.Add(builder.ToString());
                builder.Clear();
            }

            builder.Append(current);
        }

        if (builder.Length > 0)
        {
            words.Add(builder.ToString());
        }
    }

    private static IEnumerable<SourceDeclaration> DeclaredTypesIn(string path)
    {
        var relative = RepositoryLayout.Relative(path);
        var lines = File.ReadAllLines(path);
        var inBlockComment = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var code = StripComments(line, ref inBlockComment);

            if (code.Length == 0)
            {
                continue;
            }

            var match = DeclarationPattern().Match(code);

            if (!match.Success)
            {
                continue;
            }

            var kind = CollapseWhitespace(match.Groups["kind"].Value);
            var name = DeclaredName(kind, match.Groups["rest"].Value);

            if (name is not null)
            {
                yield return new SourceDeclaration(name, kind, relative, index + 1);
            }
        }
    }

    private static string? DeclaredName(string kind, string rest)
    {
        // A delegate declares its return type first, so its name is the identifier the parameter list
        // hangs off rather than the first identifier on the line.
        if (kind.StartsWith("delegate", StringComparison.Ordinal))
        {
            var open = rest.IndexOf('(', StringComparison.Ordinal);
            rest = open >= 0 ? rest[..open] : rest;

            var generic = rest.IndexOf('<', StringComparison.Ordinal);
            rest = generic >= 0 ? rest[..generic] : rest;

            var identifiers = IdentifierPattern().Matches(rest).Select(static m => m.Value).ToArray();

            return identifiers.Length > 0 ? identifiers[^1] : null;
        }

        var leading = IdentifierPattern().Match(rest);

        return leading.Success ? leading.Value : null;
    }

    private static string CollapseWhitespace(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Removes comments from one line, tracking whether a block comment is open across lines.
    /// </summary>
    /// <remarks>
    /// String literals are left alone. A declaration keyword cannot appear inside one in a position this
    /// reader would match, and stripping them would need a second state machine for verbatim and
    /// interpolated forms - complexity that buys nothing here.
    /// </remarks>
    private static string StripComments(string line, ref bool inBlockComment)
    {
        var builder = new StringBuilder(line.Length);

        for (var index = 0; index < line.Length; index++)
        {
            if (inBlockComment)
            {
                if (line[index] == '*' && index + 1 < line.Length && line[index + 1] == '/')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (line[index] == '/' && index + 1 < line.Length)
            {
                if (line[index + 1] == '/')
                {
                    break;
                }

                if (line[index + 1] == '*')
                {
                    inBlockComment = true;
                    index++;
                    continue;
                }
            }

            builder.Append(line[index]);
        }

        var code = builder.ToString().TrimEnd();

        return code.TrimStart().StartsWith('#') ? string.Empty : code;
    }

    [GeneratedRegex(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|protected|private|file|abstract|sealed|static|partial|readonly|ref|unsafe|new|extern)\s+)*(?<kind>record\s+class|record\s+struct|record|class|struct|interface|enum|delegate)\s+(?<rest>[^\r\n]*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DeclarationPattern();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
