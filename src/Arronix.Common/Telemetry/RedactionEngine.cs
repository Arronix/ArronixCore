using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Diagnostics;
using Arronix.Common.Configuration;
using Arronix.Common.Lifetimes;

namespace Arronix.Common.Telemetry;

/// <summary>
/// Masks the secrets an installation's rules describe, before anything else reads the text.
/// </summary>
/// <remarks>
/// <para>
/// Compiled once, at composition, and fail-closed: a rule that will not compile, is too long, or names a
/// capture group it does not declare is a composition failure rather than a rule that silently stops
/// redacting. An installation that cannot say what its secrets look like must not start and then log them.
/// </para>
/// <para>
/// Every pattern is <see cref="RegexOptions.NonBacktracking"/> with a match timeout. A redaction pattern
/// runs against every line the platform emits, and the one failure mode that matters is a pattern that
/// takes exponential time on a line an attacker chose.
/// </para>
/// <para>
/// Rules are additive, owner-qualified and write-only. A contributor cannot remove, weaken or shadow
/// another's rule: every rule is applied to every string, the result of one is the input to the next, and
/// masking only ever removes text. Rules an extension contributed stay in force after it is withdrawn,
/// because unlearning the shape of a secret is not a thing a withdrawal should do.
/// </para>
/// </remarks>
internal sealed class RedactionEngine : IRedactionAdmission
{
    private readonly Lock _gate = new();
    private readonly RedactionOptions _settings;
    private readonly TelemetryOptions _limits;

    /// <summary>
    /// Identifiers spoken for: prepared, and either applying or waiting to. Held from preparation so that
    /// two attempts preparing at once cannot both be told their identifiers are free.
    /// </summary>
    private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);
    private IReadOnlyList<CompiledRule> _rules;
    private int _next;

    private RedactionEngine(IReadOnlyList<CompiledRule> rules, RedactionOptions settings, TelemetryOptions limits)
    {
        _rules = rules;
        _next = rules.Count;
        _settings = settings;
        _limits = limits;
    }

    /// <summary>An engine that masks nothing, for a host that contributed no rules.</summary>
    /// <remarks>Its own instance each time: an engine is added to, so a shared empty one would not stay empty.</remarks>
    internal static RedactionEngine Empty => new([], new RedactionOptions(), new TelemetryOptions());

    /// <summary>Gets what a masked secret is replaced with, which the operator names.</summary>
    internal string Mask => _settings.Replacement;

    /// <summary>Gets the identifiers of the compiled rules, in application order.</summary>
    internal IReadOnlyList<string> RuleIds => [.. Snapshot().Select(rule => rule.Id)];

    /// <summary>
    /// Compiles every contributed rule, or fails with everything wrong with them.
    /// </summary>
    /// <param name="providers">The rule providers, host-owned first.</param>
    /// <param name="options">The bounds the installation compiles under.</param>
    /// <param name="settings">What the operator said about redaction.</param>
    /// <returns>The engine.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Any rule is unusable.</exception>
    internal static RedactionEngine Compile(
        IEnumerable<OwnedRedactionRules> providers,
        TelemetryOptions options,
        RedactionOptions? settings = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        var engine = new RedactionEngine([], settings ?? new RedactionOptions(), options);

        if (!engine.TryPrepare(providers, options, out var prepared, out var defects))
        {
            throw new InvalidOperationException(
                "The installation's redaction rules are unusable, so it would log secrets it has been told "
                + $"the shape of: {string.Join(" | ", defects)}");
        }

        engine.Commit(prepared!);
        Confirm(prepared);
        return engine;
    }

    /// <summary>
    /// Commits rules that were prepared earlier, making them apply from here on.
    /// </summary>
    /// <param name="prepared">The compiled rules a preparation produced.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prepared"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Appended, never inserted: the rules already in force run first, and a later contributor can only
    /// mask more of what they left.
    /// </remarks>
    internal void Commit(PreparedRedactionRules prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        lock (_gate)
        {
            if (prepared.Settled || prepared.Applied || prepared.Rules.Count == 0)
            {
                return;
            }

            _rules = [.. _rules, .. prepared.Rules];
            _next += prepared.Rules.Count;
            prepared.Applied = true;
        }
    }

    /// <inheritdoc />
    public bool TryPrepare(
        string owner,
        IReadOnlyList<RedactionRule> rules,
        out IRedactionCommit? prepared,
        out IReadOnlyList<string> defects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(rules);

        var admitted = TryPrepare(
            [new OwnedRedactionRules(owner, rules)],
            _limits,
            out var compiled,
            out defects);

        prepared = admitted ? new Preparation(this, compiled!) : null;
        return admitted;
    }

    /// <summary>
    /// Takes prepared rules back, whether or not they had begun applying, and frees their identifiers.
    /// </summary>
    /// <param name="prepared">The compiled rules a preparation produced.</param>
    /// <remarks>
    /// A commit is provisional until the attempt that made it has finished publishing. Redaction can only
    /// widen, so a rule that applied for the length of a failed publication masked more than it had to and
    /// revealed nothing; what would be wrong is leaving it applying, and leaving its identifier taken so
    /// the same package could not try again.
    /// </remarks>
    internal void Rollback(PreparedRedactionRules? prepared)
    {
        if (prepared is null)
        {
            return;
        }

        lock (_gate)
        {
            if (prepared.Settled)
            {
                return;
            }

            if (prepared.Applied)
            {
                _rules = [.. _rules.Where(rule => !prepared.Rules.Contains(rule))];
                prepared.Applied = false;
            }

            foreach (var rule in prepared.Rules)
            {
                _reserved.Remove(rule.Id);
            }

            prepared.Settled = true;
        }
    }

    /// <summary>Settles a preparation for good: what it applied stays, and its identifiers stay taken.</summary>
    /// <param name="prepared">The compiled rules a preparation produced.</param>
    internal static void Confirm(PreparedRedactionRules? prepared)
    {
        if (prepared is not null)
        {
            prepared.Settled = true;
        }
    }

    /// <summary>
    /// Compiles one contributor's rules without applying them yet, or reports everything wrong with them.
    /// </summary>
    /// <param name="providers">The contributions, each qualified by its owner.</param>
    /// <param name="options">The bounds the installation compiles under.</param>
    /// <param name="prepared">The compiled rules, to commit or discard later.</param>
    /// <param name="defects">Everything wrong, or an empty list.</param>
    /// <returns><see langword="true"/> when every rule compiled.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// All-or-nothing per call: a contributor whose rules are half-usable has described a secret it cannot
    /// mask, and admitting the half that compiled would leave the platform reporting redaction it is not
    /// doing.
    /// </para>
    /// <para>
    /// Preparing applies nothing; it does reserve the identifiers, so two attempts preparing at once cannot
    /// both be told the same identifier is free. A package whose later checks fail, or whose publication
    /// fails, rolls back and leaves the live rule set exactly as it found it, identifiers included.
    /// </para>
    /// </remarks>
    internal bool TryPrepare(
        IEnumerable<OwnedRedactionRules> providers,
        TelemetryOptions options,
        out PreparedRedactionRules? prepared,
        out IReadOnlyList<string> defects)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        prepared = null;

        lock (_gate)
        {
            var compiled = new List<CompiledRule>();
            var found = new List<string>();
            var order = _next;

            foreach (var owned in providers)
            {
                foreach (var rule in owned.Rules)
                {
                    if (rule is null)
                    {
                        found.Add($"'{owned.Owner}' contributed a null redaction rule.");
                        continue;
                    }

                    // Qualified by owner: two contributors may both call a rule "api-key", and neither may
                    // take the other's identifier and thereby replace its rule.
                    var id = $"{owned.Owner}/{rule.RuleId}";

                    // An operator may switch one rule off where it produces false positives, and names it
                    // by its qualified identifier: an unqualified name would switch off every contributor's
                    // rule that happens to share it, including the host's.
                    if (_settings.DisabledRuleIds.Contains(id, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    if (_reserved.Contains(id)
                        || compiled.Any(existing => string.Equals(existing.Id, id, StringComparison.Ordinal)))
                    {
                        found.Add($"Redaction rule '{id}' is contributed more than once.");
                        continue;
                    }

                    if (_reserved.Count + compiled.Count >= options.MaxRedactionRules)
                    {
                        found.Add(
                            $"Redaction rule '{id}' exceeds the installation's limit of "
                            + $"{options.MaxRedactionRules} rules.");
                        continue;
                    }

                    if (TryCompile(id, rule, options, out var ready, out var defect))
                    {
                        compiled.Add(ready! with { Order = order++ });
                    }
                    else
                    {
                        found.Add(defect!);
                    }
                }
            }

            defects = found;

            if (found.Count > 0)
            {
                return false;
            }

            foreach (var rule in compiled)
            {
                _reserved.Add(rule.Id);
            }

            prepared = new PreparedRedactionRules(compiled);
            return true;
        }
    }

    /// <summary>
    /// Masks every secret this engine's rules describe.
    /// </summary>
    /// <param name="text">The text to redact.</param>
    /// <returns>The redacted text, or the argument when nothing matched.</returns>
    /// <remarks>
    /// A rule that times out, or fails for any other reason the process can survive, masks the whole string
    /// rather than returning it. A rule the installation declared was about a secret, and text a rule could
    /// not finish reading is text nobody has cleared.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Text a rule could not finish reading is masked rather than returned; a failure here must never produce unredacted output.")]
    internal string Redact(string? text)
    {
        var rules = Snapshot();

        if (string.IsNullOrEmpty(text) || rules.Count == 0 || !_settings.Enabled)
        {
            return text ?? string.Empty;
        }

        var current = text;

        foreach (var rule in rules)
        {
            try
            {
                current = rule.Pattern.Replace(current, match => Masked(match, rule.SecretGroup, Mask));
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                return Mask;
            }
        }

        return current;
    }

    private IReadOnlyList<CompiledRule> Snapshot()
    {
        lock (_gate)
        {
            return _rules;
        }
    }

    /// <remarks>
    /// A match whose secret group did not participate masks the whole match. The rule said this text is
    /// where a secret appears; a capture that did not fire is a rule that cannot say which part, not a rule
    /// that has decided there is nothing to hide.
    /// </remarks>
    private static string Masked(Match match, string group, string mask)
    {
        var secret = match.Groups[group];

        if (!secret.Success)
        {
            return mask;
        }

        var offset = secret.Index - match.Index;
        return string.Concat(
            match.Value.AsSpan(0, offset),
            mask,
            match.Value.AsSpan(offset + secret.Length));
    }

    private static bool TryCompile(
        string id,
        RedactionRule rule,
        TelemetryOptions options,
        out CompiledRule? compiled,
        out string? defect)
    {
        compiled = null;
        defect = null;

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            defect = $"Redaction rule '{id}' declares no pattern.";
            return false;
        }

        if (rule.Pattern.Length > options.MaxRedactionPatternLength)
        {
            defect =
                $"Redaction rule '{id}' declares a pattern of {rule.Pattern.Length} characters, and the "
                + $"installation compiles patterns up to {options.MaxRedactionPatternLength}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.SecretGroupName))
        {
            defect = $"Redaction rule '{id}' names no capture group holding the secret.";
            return false;
        }

        var syntax = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

        if (rule.IgnoreCase)
        {
            syntax |= RegexOptions.IgnoreCase;
        }

        Regex pattern;

        try
        {
            pattern = new Regex(rule.Pattern, syntax, options.RedactionTimeout);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException)
        {
            // NotSupportedException is what a lookaround gets: the engine is non-backtracking by policy, so
            // a pattern needing backtracking is refused here rather than throwing at the first log line.
            defect = $"Redaction rule '{id}' does not compile: {failure.Message}";
            return false;
        }

        if (!pattern.GetGroupNames().Contains(rule.SecretGroupName, StringComparer.Ordinal))
        {
            defect =
                $"Redaction rule '{id}' names the capture group '{rule.SecretGroupName}', which its pattern "
                + "does not declare. A rule that captures nothing masks nothing.";
            return false;
        }

        compiled = new CompiledRule(id, pattern, rule.SecretGroupName, Order: 0);
        return true;
    }

    internal sealed record CompiledRule(string Id, Regex Pattern, string SecretGroup, int Order);

    /// <summary>One prepared set, bound to the engine that compiled it.</summary>
    private sealed class Preparation(RedactionEngine engine, PreparedRedactionRules prepared) : IRedactionCommit
    {
        public void Commit() => engine.Commit(prepared);

        public void Rollback() => engine.Rollback(prepared);

        public void Confirm() => RedactionEngine.Confirm(prepared);
    }

    /// <summary>
    /// Rules that compiled but are not applying yet, held until the attempt that contributed them either
    /// publishes or does not.
    /// </summary>
    internal sealed class PreparedRedactionRules
    {
        internal PreparedRedactionRules(IReadOnlyList<CompiledRule> rules) => Rules = rules;

        /// <summary>Gets the rules this preparation would add.</summary>
        internal IReadOnlyList<CompiledRule> Rules { get; }

        /// <summary>Gets or sets a value indicating whether these rules are currently applying.</summary>
        internal bool Applied { get; set; }

        /// <summary>Gets or sets a value indicating whether this preparation can still be taken back.</summary>
        internal bool Settled { get; set; }
    }
}

/// <summary>
/// One contributor's redaction rules, and who contributed them.
/// </summary>
/// <param name="Owner">The contributor, which qualifies every rule identifier.</param>
/// <param name="Rules">What it contributed.</param>
internal sealed record OwnedRedactionRules(string Owner, IReadOnlyList<RedactionRule> Rules);
