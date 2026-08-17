using System.Globalization;
using System.Reflection;
using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// Moves one family's facts between the typed form a family writes and the erased point everything else
/// holds.
/// </summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// <para>
/// The projection is invertible, and that is load-bearing rather than incidental. A reading carries a
/// member's <i>declared rank</i>, which is the enumeration member's own numeric value, so a point holds
/// everything the facts held and nothing is lost on the way out. Without that, a size model declared over
/// the typed facts could never be called from a member that takes a point, and the family would need two
/// size models that could disagree.
/// </para>
/// <para>
/// Two things it deliberately does not do. It does not invent a reading for an axis the facts left absent —
/// absence is a state, and the whole framework exists so that nothing has to guess on its behalf. And it
/// does not re-derive an axis identity from anything but the property name, because a second derivation is
/// a second thing to drift.
/// </para>
/// </remarks>
internal sealed class QualityFactsProjection<TFacts>
    where TFacts : IQualityFacts
{
    private readonly FormatFamilyId family;
    private readonly IReadOnlyList<DeclaredAxis> axes;
    private readonly Func<TFacts, AxisReading>[] readers;
    private readonly Func<AxisReading, object?>[] writers;

    /// <summary>
    /// Initializes a new instance of the <see cref="QualityFactsProjection{TFacts}"/> class.
    /// </summary>
    /// <param name="family">The family the points belong to.</param>
    /// <param name="axes">The declared axes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="axes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The facts type is not a class with a parameterless constructor.</exception>
    internal QualityFactsProjection(FormatFamilyId family, IReadOnlyList<DeclaredAxis> axes)
    {
        ArgumentNullException.ThrowIfNull(axes);

        if (!typeof(TFacts).IsClass || typeof(TFacts).GetConstructor(Type.EmptyTypes) is null)
        {
            throw new ArgumentException(
                $"'{typeof(TFacts).Name}' is a quality-facts type, so the host has to be able to rebuild one "
                + "from a stored point. That needs a class with a parameterless constructor and initializable "
                + "axis properties.",
                nameof(axes));
        }

        this.family = family;
        this.axes = axes;
        readers = new Func<TFacts, AxisReading>[axes.Count];
        writers = new Func<AxisReading, object?>[axes.Count];

        for (var index = 0; index < axes.Count; index++)
        {
            readers[index] = Reader(axes[index]);
            writers[index] = Writer(axes[index]);
        }
    }

    /// <summary>Projects typed facts onto a point.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="facts"/> is <see langword="null"/>.</exception>
    internal QualityPoint Project(TFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var readings = new AxisReading[readers.Length];

        for (var index = 0; index < readers.Length; index++)
        {
            readings[index] = readers[index](facts);
        }

        return new QualityPoint { Family = family, Readings = readings };
    }

    /// <summary>Rebuilds typed facts from a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The facts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// An axis the point says nothing about comes back absent, which is what it was. A point holding a
    /// reading for an axis this family does not declare is ignored rather than rejected: a point outlives a
    /// contract revision, and a stored reading for an axis that no longer exists is not a reason to refuse
    /// to read the file.
    /// </remarks>
    internal TFacts Materialize(QualityPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var facts = (TFacts)Activator.CreateInstance(typeof(TFacts))!;

        for (var index = 0; index < writers.Length; index++)
        {
            var declared = axes[index];
            var value = writers[index](point[declared.Axis.Id]);

            if (value is not null)
            {
                declared.Property.SetValue(facts, value);
            }
        }

        return facts;
    }

    private static Func<TFacts, AxisReading> Reader(DeclaredAxis declared) =>
        declared.Kind switch
        {
            AxisValueShape.Member => Bind<Func<TFacts, AxisReading>>(
                nameof(MemberReader), declared.ValueType, declared),

            AxisValueShape.MemberSet => Bind<Func<TFacts, AxisReading>>(
                nameof(SetReader), declared.ValueType, declared),

            _ => declared.ValueType == typeof(int)
                ? WholeNumberReader(declared)
                : RealNumberReader(declared),
        };

    private static Func<AxisReading, object?> Writer(DeclaredAxis declared) =>
        declared.Kind switch
        {
            AxisValueShape.Member => Bind<Func<AxisReading, object?>>(
                nameof(MemberWriter), declared.ValueType, declared),

            AxisValueShape.MemberSet => Bind<Func<AxisReading, object?>>(
                nameof(SetWriter), declared.ValueType, declared),

            _ => declared.ValueType == typeof(int)
                ? WholeNumberWriter()
                : RealNumberWriter(),
        };

    private static TDelegate Bind<TDelegate>(string builder, Type valueType, DeclaredAxis declared)
        where TDelegate : Delegate =>
        (TDelegate)typeof(QualityFactsProjection<TFacts>)
            .GetMethod(builder, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(valueType)
            .Invoke(null, [declared])!;

    private static Func<TFacts, AxisReading> MemberReader<TValue>(DeclaredAxis declared)
        where TValue : struct, Enum
    {
        var id = declared.Axis.Id;
        var read = declared.Property.GetMethod!.CreateDelegate<Func<TFacts, Evidence<TValue>>>();

        return facts =>
        {
            var evidence = read(facts);

            return evidence.TryGet(out var value)
                ? AxisReading.Of(id, Member(value), evidence.Source)
                : AxisReading.Absent(id);
        };
    }

    private static Func<TFacts, AxisReading> SetReader<TValue>(DeclaredAxis declared)
        where TValue : struct, Enum
    {
        var id = declared.Axis.Id;
        var read = declared.Property.GetMethod!.CreateDelegate<Func<TFacts, EvidenceSet<TValue>>>();

        return facts =>
        {
            var evidence = read(facts);

            if (!evidence.IsKnown)
            {
                return AxisReading.Absent(id);
            }

            var members = new AxisValue[evidence.Members.Count];

            for (var index = 0; index < members.Length; index++)
            {
                members[index] = Member(evidence.Members[index]);
            }

            return AxisReading.OfMany(id, evidence.Source, members);
        };
    }

    private static Func<TFacts, AxisReading> WholeNumberReader(DeclaredAxis declared)
    {
        var id = declared.Axis.Id;
        var read = declared.Property.GetMethod!.CreateDelegate<Func<TFacts, Evidence<int>>>();

        return facts =>
        {
            var evidence = read(facts);

            return evidence.TryGet(out var value)
                ? AxisReading.Of(id, AxisValue.Quantity(value), evidence.Source)
                : AxisReading.Absent(id);
        };
    }

    private static Func<TFacts, AxisReading> RealNumberReader(DeclaredAxis declared)
    {
        var id = declared.Axis.Id;
        var read = declared.Property.GetMethod!.CreateDelegate<Func<TFacts, Evidence<double>>>();

        return facts =>
        {
            var evidence = read(facts);

            return evidence.TryGet(out var value)
                ? AxisReading.Of(id, AxisValue.Quantity(value), evidence.Source)
                : AxisReading.Absent(id);
        };
    }

    private static Func<AxisReading, object?> MemberWriter<TValue>(DeclaredAxis declared)
        where TValue : struct, Enum
    {
        var valueType = declared.ValueType;

        return reading => !reading.IsKnown || reading.Values.Count == 0
            ? null
            : Evidence<TValue>.From(
                (TValue)Enum.ToObject(valueType, reading.Values[0].DeclaredRank),
                reading.Source);
    }

    private static Func<AxisReading, object?> SetWriter<TValue>(DeclaredAxis declared)
        where TValue : struct, Enum
    {
        var valueType = declared.ValueType;

        return reading =>
        {
            if (!reading.IsKnown)
            {
                return null;
            }

            var members = new TValue[reading.Values.Count];

            for (var index = 0; index < members.Length; index++)
            {
                members[index] = (TValue)Enum.ToObject(valueType, reading.Values[index].DeclaredRank);
            }

            return EvidenceSet<TValue>.Of(reading.Source, members);
        };
    }

    private static Func<AxisReading, object?> WholeNumberWriter() =>
        reading => !reading.IsKnown || reading.Values.Count == 0
            ? null
            : Evidence<int>.From((int)Math.Round(reading.Values[0].Magnitude), reading.Source);

    private static Func<AxisReading, object?> RealNumberWriter() =>
        reading => !reading.IsKnown || reading.Values.Count == 0
            ? null
            : Evidence<double>.From(reading.Values[0].Magnitude, reading.Source);

    private static AxisValue Member<TValue>(TValue value)
        where TValue : struct, Enum =>
        AxisValue.Member(Convert.ToInt32(value, CultureInfo.InvariantCulture), value.ToString());
}
