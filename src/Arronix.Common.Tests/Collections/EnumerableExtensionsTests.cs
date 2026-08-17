using System;
using System.Collections.Generic;
using Arronix.Common.Collections;

namespace Arronix.Common.Tests.Collections;

/// <summary>
/// Covers the three surviving sequence helpers.
/// </summary>
[TestFixture]
public class EnumerableExtensionsTests
{
    [Test]
    public void AddIfNotNull_AddsAValue()
    {
        var target = new List<string>();

        target.AddIfNotNull("value");

        Assert.That(target, Is.EqualTo(new[] { "value" }));
    }

    [Test]
    public void AddIfNotNull_IgnoresAMissingValue()
    {
        var target = new List<string>();

        target.AddIfNotNull(null);

        Assert.That(target, Is.Empty);
    }

    [Test]
    public void AddIfNotNull_AcceptsAnyCollection()
    {
        var target = new HashSet<string>();

        target.AddIfNotNull("value");
        target.AddIfNotNull(null);

        Assert.That(target, Is.EqualTo(new[] { "value" }));
    }

    [Test]
    public void AddIfNotNull_RejectsAMissingCollection()
    {
        Assert.That(() => ((List<string>)null!).AddIfNotNull("value"), Throws.ArgumentNullException);
    }

    [Test]
    public void None_ReportsTrueWhenNothingMatches()
    {
        Assert.That(new[] { 1, 3, 5 }.None(value => value % 2 == 0), Is.True);
    }

    [Test]
    public void None_ReportsFalseWhenSomethingMatches()
    {
        Assert.That(new[] { 1, 2, 3 }.None(value => value % 2 == 0), Is.False);
    }

    [Test]
    public void None_ReportsTrueForAnEmptySequence()
    {
        Assert.That(Array.Empty<int>().None(_ => true), Is.True);
    }

    [Test]
    public void ToDictionaryIgnoreDuplicates_KeepsTheFirstItemForEachKey()
    {
        var source = new[] { "apple", "avocado", "banana" };

        var result = source.ToDictionaryIgnoreDuplicates(item => item[0]);

        Assert.That(result['a'], Is.EqualTo("apple"));
        Assert.That(result['b'], Is.EqualTo("banana"));
        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void ToDictionaryIgnoreDuplicates_HonorsASuppliedComparer()
    {
        var source = new[] { "Alpha", "alpha", "Beta" };

        var result = source.ToDictionaryIgnoreDuplicates(
            item => item,
            StringComparer.OrdinalIgnoreCase);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result["ALPHA"], Is.EqualTo("Alpha"));
    }

    [Test]
    public void ToDictionaryIgnoreDuplicates_ProjectsTheValueOnlyForTheItemThatWinsItsKey()
    {
        var source = new[] { "apple", "avocado" };
        var projections = 0;

        var result = source.ToDictionaryIgnoreDuplicates(
            item => item[0],
            item =>
            {
                projections++;
                return item.Length;
            });

        Assert.That(result['a'], Is.EqualTo("apple".Length));
        Assert.That(projections, Is.EqualTo(1));
    }

    [Test]
    public void ToDictionaryIgnoreDuplicates_RejectsAMissingSelector()
    {
        Assert.That(
            () => new[] { 1 }.ToDictionaryIgnoreDuplicates((Func<int, int>)null!),
            Throws.ArgumentNullException);
    }
}
