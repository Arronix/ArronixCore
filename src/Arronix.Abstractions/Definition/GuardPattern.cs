
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One named guard expression, declared once and referenced by identifier.
/// </summary>
/// <param name="GuardId">The identifier guard references use.</param>
/// <param name="Regex">The regular expression.</param>
/// <param name="Input">Which form of the text the expression runs against.</param>
/// <param name="CaseSensitive">
/// Whether case is significant. The default is insensitive; the sensitive form exists because the
/// surveyed sources rely on it — an upper-case token can be a revision marker where the same word in
/// lower case is just a word, and a default-insensitive engine would silently flatten the two.
/// </param>
public readonly record struct GuardPattern(
    string GuardId,
    string Regex,
    GuardInput Input = GuardInput.Normalized,
    bool CaseSensitive = false);
