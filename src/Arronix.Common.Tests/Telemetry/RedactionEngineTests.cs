using System;
using System.Collections.Generic;
using Arronix.Abstractions.Diagnostics;
using Arronix.Common.Configuration;
using Arronix.Common.Telemetry;

namespace Arronix.Common.Tests.Telemetry;

/// <summary>
/// What the redaction engine compiles, what it refuses to compile, and what it does when a rule misbehaves.
/// </summary>
[TestFixture]
public class RedactionEngineTests
{
    [Test]
    public void ASecretIsMaskedAndTheTextAroundItSurvives()
    {
        var engine = Compile(Rule("token", "token=(?<secret>[A-Za-z0-9]+)"));

        Assert.That(engine.Redact("using token=abc123 to connect"), Is.EqualTo("using token=(redacted) to connect"));
    }

    [Test]
    public void ARuleThatDoesNotCompileFailsTheInstallationRatherThanGoingQuiet()
    {
        var broken = () => Compile(Rule("bad", "(?<secret>[unclosed"));

        Assert.That(
            broken,
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("does not compile"),
            "an installation that cannot say what its secrets look like must not start and then log them");
    }

    [Test]
    public void APatternTheEngineCannotSupportIsRefusedAtCompileTimeRatherThanAtTheFirstLogLine()
    {
        // A lookaround needs backtracking, and this engine is non-backtracking by policy.
        var unsupported = () => Compile(Rule("lookahead", "(?=x)(?<secret>[a-z]+)"));

        Assert.That(unsupported, Throws.TypeOf<InvalidOperationException>().With.Message.Contains("does not compile"));
    }

    [Test]
    public void ARuleThatCapturesNothingIsRefused()
    {
        var missing = () => Compile(Rule("no-group", "token=[A-Za-z0-9]+"));

        Assert.That(missing, Throws.TypeOf<InvalidOperationException>().With.Message.Contains("does not declare"));
    }

    [Test]
    public void ARuleLongerThanTheInstallationCompilesIsRefused()
    {
        var long_ = () => Compile(
            Rule("long", "(?<secret>" + new string('a', 600) + ")"),
            new TelemetryOptions { MaxRedactionPatternLength = 64 });

        Assert.That(long_, Throws.TypeOf<InvalidOperationException>().With.Message.Contains("characters"));
    }

    [Test]
    public void TwoContributorsMayNameARuleTheSameAndNeitherReplacesTheOther()
    {
        var engine = Compile(
            new OwnedRedactionRules("host", [new RedactionRule("api-key", "host=(?<secret>[a-z]+)")]),
            new OwnedRedactionRules("a.movies", [new RedactionRule("api-key", "mine=(?<secret>[a-z]+)")]));

        Assert.Multiple(() =>
        {
            Assert.That(engine.RuleIds, Is.EqualTo(new[] {"host/api-key", "a.movies/api-key"}));
            Assert.That(engine.Redact("host=aaa mine=bbb"), Is.EqualTo("host=(redacted) mine=(redacted)"));
        });
    }

    [Test]
    public void AnOperatorSwitchesOneRuleOffByItsQualifiedName()
    {
        var settings = new RedactionOptions();
        settings.DisabledRuleIds.Add("a.movies/api-key");

        var engine = Compile(
            [
                new OwnedRedactionRules("host", [new RedactionRule("api-key", "host=(?<secret>[a-z]+)")]),
                new OwnedRedactionRules("a.movies", [new RedactionRule("api-key", "mine=(?<secret>[a-z]+)")]),
            ],
            settings);

        Assert.Multiple(() =>
        {
            Assert.That(engine.RuleIds, Is.EqualTo(new[] {"host/api-key"}));
            Assert.That(
                engine.Redact("host=aaa mine=bbb"),
                Is.EqualTo("host=(redacted) mine=bbb"),
                "an unqualified name would have switched off the host's rule as well");
        });
    }

    [Test]
    public void AnUnqualifiedNameSwitchesNothingOff()
    {
        var settings = new RedactionOptions();
        settings.DisabledRuleIds.Add("api-key");

        var engine = Compile(
            [new OwnedRedactionRules("host", [new RedactionRule("api-key", "host=(?<secret>[a-z]+)")])],
            settings);

        Assert.That(engine.RuleIds, Is.EqualTo(new[] {"host/api-key"}));
    }

    [Test]
    public void AMatchWhoseSecretGroupDidNotFireMasksTheWholeMatch()
    {
        // The rule says a secret appears here. A capture that did not fire cannot say which part of it is
        // the secret; it has not decided there is nothing to hide.
        var engine = Compile(Rule("either", "token=(?:(?<secret>[0-9]+)|anonymous)"));

        Assert.That(engine.Redact("token=anonymous"), Is.EqualTo("(redacted)"));
    }

    [Test]
    public void TurningRedactionOffIsTheOperatorsToDo()
    {
        var engine = Compile(
            [new OwnedRedactionRules("host", [new RedactionRule("token", "token=(?<secret>[a-z]+)")])],
            new RedactionOptions { Enabled = false });

        Assert.That(engine.Redact("token=abc"), Is.EqualTo("token=abc"));
    }

    [Test]
    public void RulesPreparedByAnAttemptThatNeverPublishedApplyToNothing()
    {
        var engine = Compile(Rule("host", "host=(?<secret>[a-z]+)"));

        Assert.That(
            engine.TryPrepare(
                [new OwnedRedactionRules("a.movies", [new RedactionRule("mine", "mine=(?<secret>[a-z]+)")])],
                new TelemetryOptions(),
                out var prepared,
                out _),
            Is.True);

        engine.Rollback(prepared);

        Assert.Multiple(() =>
        {
            Assert.That(engine.RuleIds, Is.EqualTo(new[] {"host/host"}));
            Assert.That(engine.Redact("mine=bbb"), Is.EqualTo("mine=bbb"), "nothing was ever applying it");
        });
    }

    [Test]
    public void RulesPreparedAndThenCommittedApply()
    {
        var engine = Compile(Rule("host", "host=(?<secret>[a-z]+)"));

        engine.TryPrepare(
            [new OwnedRedactionRules("a.movies", [new RedactionRule("mine", "mine=(?<secret>[a-z]+)")])],
            new TelemetryOptions(),
            out var prepared,
            out _);

        engine.Commit(prepared!);

        Assert.That(engine.Redact("mine=bbb"), Is.EqualTo("mine=(redacted)"));
    }

    [TestCase("Authorization: Bearer abc.def", "abc.def")]
    [TestCase("https://user:hunter2@example/x", "hunter2")]
    [TestCase("apiKey=zzz999&page=2", "zzz999")]
    [TestCase("{\"password\": \"hunter2\", \"user\": \"ada\"}", "hunter2")]
    [TestCase("X-Api-Key: k-9f8e7d", "k-9f8e7d")]
    [TestCase("Cookie: session=abc; other=def", "abc")]
    [TestCase("token eyJhbGciOi.eyJzdWIiOi.SflKxwRJSM", "eyJhbGciOi.eyJzdWIiOi.SflKxwRJSM")]
    [TestCase("Server=db;Password=hunter2;Trusted=false", "hunter2")]
    [TestCase("connected to 203.0.113.7", "203.0.113.7")]
    [TestCase("connected to 2001:db8::1", "2001:db8::1")]
    [TestCase("connected to ::1", "::1")]
    public void TheRulesThePlatformShipsMaskWhatEveryInstallationHas(string line, string secret)
    {
        Assert.That(Core().Redact(line), Does.Not.Contain(secret));
    }

    [TestCase("the token bucket refilled after 30s")]
    [TestCase("version 1.2.3.4 of the parser")]
    [TestCase("retried 3 times over 2 hosts")]
    [TestCase("scanned 12 files in 3 folders")]
    [TestCase("started at 09:15:00 and finished at 09:16:30")]
    public void TheRulesThePlatformShipsLeaveOrdinaryTextAlone(string line)
    {
        Assert.That(Core().Redact(line), Is.EqualTo(line));
    }

    [Test]
    public void APemBlockLosesItsBodyAndKeepsItsMarkers()
    {
        var redacted = Core().Redact("-----BEGIN PRIVATE KEY-----\nMIIBVgIBADAN\n-----END PRIVATE KEY-----");

        Assert.Multiple(() =>
        {
            Assert.That(redacted, Does.Not.Contain("MIIBVgIBADAN"));
            Assert.That(redacted, Does.Contain("BEGIN PRIVATE KEY"));
        });
    }

    [Test]
    public void AnOperatorWhoKeepsAddressesKeepsThem()
    {
        var engine = Core(new RedactionOptions { MaskNetworkAddresses = false });

        Assert.That(engine.Redact("connected to 203.0.113.7"), Does.Contain("203.0.113.7"));
    }

    private static RedactionEngine Core(RedactionOptions? settings = null)
    {
        var chosen = settings ?? new RedactionOptions();

        return RedactionEngine.Compile(
            [new OwnedRedactionRules(CoreRedactionRules.Owner, new CoreRedactionRules(chosen).Rules)],
            new TelemetryOptions(),
            chosen);
    }

    private static OwnedRedactionRules Rule(string id, string pattern)
        => new("host", [new RedactionRule(id, pattern)]);

    private static RedactionEngine Compile(params OwnedRedactionRules[] rules)
        => RedactionEngine.Compile(rules, new TelemetryOptions());

    private static RedactionEngine Compile(OwnedRedactionRules rules, TelemetryOptions options)
        => RedactionEngine.Compile([rules], options);

    private static RedactionEngine Compile(IReadOnlyList<OwnedRedactionRules> rules, RedactionOptions settings)
        => RedactionEngine.Compile(rules, new TelemetryOptions(), settings);
}
