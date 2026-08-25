using Arronix.Abstractions.Diagnostics;

namespace Arronix.Common.Telemetry;

/// <summary>
/// How a contributor's redaction rules join the installation's, one attempt at a time.
/// </summary>
/// <remarks>
/// Preparing compiles, checks and reserves the identifiers; it applies nothing. Committing applies the
/// rules provisionally: an attempt that fails afterwards rolls back, and the rules are taken out again
/// along with their identifiers, so a second attempt by the same package is not refused for colliding with
/// its own earlier one. Confirming is what makes a commit permanent, and belongs after the last step that
/// can still fail.
/// </remarks>
public interface IRedactionAdmission
{
    /// <summary>
    /// Compiles one contributor's rules without applying them.
    /// </summary>
    /// <param name="owner">Who contributed them. Every rule identifier is qualified by it.</param>
    /// <param name="rules">The rules.</param>
    /// <param name="prepared">What to commit or roll back, when they all compiled.</param>
    /// <param name="defects">Everything wrong with them, or an empty list.</param>
    /// <returns><see langword="true"/> when every rule compiled.</returns>
    bool TryPrepare(
        string owner,
        IReadOnlyList<RedactionRule> rules,
        out IRedactionCommit? prepared,
        out IReadOnlyList<string> defects);
}

/// <summary>
/// One prepared set of redaction rules, waiting to be applied or discarded.
/// </summary>
public interface IRedactionCommit
{
    /// <summary>Applies the rules from here on, provisionally. Idempotent.</summary>
    void Commit();

    /// <summary>
    /// Takes them back, whether or not they had begun applying, and frees their identifiers. Idempotent.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Settles the commit for good, once nothing that could still fail remains. Idempotent.
    /// </summary>
    void Confirm();
}
