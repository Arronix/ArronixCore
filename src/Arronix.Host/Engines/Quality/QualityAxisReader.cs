using System.Globalization;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Quality;
using Arronix.Host.Media.Typed;

// Reads the experimental display prose (ARX0020) and produces the experimental quality vocabulary (ARX0021).
#pragma warning disable ARX0020
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// Turns a quality-facts type into the axes the host and the client hold.
/// </summary>
/// <remarks>
/// <para>
/// Three sources and no fourth: the property's <i>name</i>, which is the axis identity; its <i>type</i>,
/// which gives the form and, for a closed axis, the declared order; and its <see cref="AxisAttribute"/>,
/// which gives the polarity and the unit. Nothing here says where an axis sits in anybody's preference,
/// because that relates one axis to another and is therefore policy.
/// </para>
/// <para>
/// The derivation rules are the analyzer's, restated at load time. An analyzer covers a facts type written
/// in source; it does not cover one an extension compiled against an older contract, and a guarantee that
/// only holds when a particular analyzer ran is not a guarantee. Both are needed, and the messages here
/// name the rule so that a failure at load reads the same as a failure at compile.
/// </para>
/// </remarks>
internal static class QualityAxisReader
{
    /// <summary>
    /// Reads every axis a quality-facts type declares, in declaration order.
    /// </summary>
    /// <param name="factsType">The facts type.</param>
    /// <returns>The axes, each paired with the property that declares it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factsType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A property carrying <see cref="AxisAttribute"/> is not a reading; a quantity is declared unordered; a
    /// set is declared ordered; the type declares no axis at all; or two axes derive one identity.
    /// </exception>
    internal static IReadOnlyList<DeclaredAxis> Read(Type factsType)
    {
        ArgumentNullException.ThrowIfNull(factsType);

        var axes = new List<DeclaredAxis>();
        var seen = new HashSet<QualityAxisId>();

        // Declaration order is what a point's readings are stored in and what a diagnostic view spells, so
        // it has to be the order the author wrote rather than whatever order reflection happens to hand
        // back. The metadata token is that order, and it is stable across runs.
        var declared = factsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(static property => property.MetadataToken);

        foreach (var property in declared)
        {
            if (property.GetCustomAttribute<AxisAttribute>() is not { } attribute)
            {
                continue;
            }

            var axis = Derive(factsType, property, attribute);

            if (!seen.Add(axis.Axis.Id))
            {
                throw new ArgumentException(
                    $"'{factsType.Name}' derives the axis identity '{axis.Axis.Id}' twice. An axis identity "
                    + "comes from the property name, so two axes with one identity are two properties with "
                    + "one name.",
                    nameof(factsType));
            }

            axes.Add(axis);
        }

        return axes.Count > 0
            ? axes
            : throw new ArgumentException(
                $"'{factsType.Name}' declares no quality axis. A facts type with no axis states nothing "
                + "about a file, and every policy over it would compare every file equal.",
                nameof(factsType));
    }

    private static DeclaredAxis Derive(Type factsType, PropertyInfo property, AxisAttribute attribute)
    {
        var id = QualityAxisId.FromProperty(property.Name);
        var display = property.GetCustomAttribute<DisplayAttribute>();
        var declared = property.PropertyType;

        var (kind, valueType) = ShapeOf(factsType, property, declared);
        var form = FormOf(factsType, property, attribute, kind, valueType);

        var axis = new QualityAxis
        {
            Id = id,
            Name = display?.Name ?? DerivedNames.Label(property.Name),
            Description = display?.Description,
            Form = form,
            GreaterIsRicher = attribute.Ordering != AxisOrdering.Descending,
            Multivalued = kind == AxisValueShape.MemberSet,
            Unit = attribute.Unit,
            Members = form == AxisForm.Scalar ? [] : MembersOf(valueType),
        };

        return new DeclaredAxis(axis, property, kind, valueType);
    }

    private static (AxisValueShape Kind, Type ValueType) ShapeOf(
        Type factsType,
        PropertyInfo property,
        Type declared)
    {
        if (declared.IsGenericType)
        {
            var definition = declared.GetGenericTypeDefinition();
            var argument = declared.GetGenericArguments()[0];

            if (definition == typeof(EvidenceSet<>))
            {
                return (AxisValueShape.MemberSet, argument);
            }

            if (definition == typeof(Evidence<>))
            {
                if (argument.IsEnum)
                {
                    return (AxisValueShape.Member, argument);
                }

                if (argument == typeof(int) || argument == typeof(double))
                {
                    return (AxisValueShape.Quantity, argument);
                }
            }
        }

        throw new ArgumentException(
            $"'{factsType.Name}.{property.Name}' carries an axis attribute over '{declared.Name}'. An axis "
            + "is a reading of an enumeration, of a whole number, of a real number, or a set of enumeration "
            + "members. A boolean is none of those: two states with a preferred one is an enumeration whose "
            + "order is stated, and stating it is the point.",
            nameof(factsType));
    }

    private static AxisForm FormOf(
        Type factsType,
        PropertyInfo property,
        AxisAttribute attribute,
        AxisValueShape kind,
        Type valueType)
    {
        var unordered = attribute.Ordering == AxisOrdering.Unordered;

        if (kind == AxisValueShape.Quantity && unordered)
        {
            throw new ArgumentException(
                $"'{factsType.Name}.{property.Name}' is a quantity declared unordered, which leaves its "
                + $"comparison undefined. A quantity of '{valueType.Name}' orders whether anyone wants it "
                + "to or not; whether the user prefers more of it is policy.",
                nameof(factsType));
        }

        if (kind == AxisValueShape.MemberSet && !unordered)
        {
            throw new ArgumentException(
                $"'{factsType.Name}.{property.Name}' is a set declared ordered, which leaves its comparison "
                + "undefined: two sets that overlap have no greater and no lesser. A set axis states "
                + "membership, and membership is refused, required or scored — never ranked.",
                nameof(factsType));
        }

        return kind switch
        {
            AxisValueShape.Quantity => AxisForm.Scalar,
            AxisValueShape.MemberSet => AxisForm.Nominal,
            _ => unordered ? AxisForm.Nominal : AxisForm.Ordinal,
        };
    }

    private static IReadOnlyList<AxisValue> MembersOf(Type enumType) =>
        [.. EnumOrder.Names(enumType).Select(name => AxisValue.Member(RankOf(enumType, name), name))];

    private static int RankOf(Type enumType, string name) =>
        Convert.ToInt32(Enum.Parse(enumType, name), CultureInfo.InvariantCulture);
}

/// <summary>
/// One axis, and the property it was read from.
/// </summary>
/// <param name="Axis">The axis as a consumer sees it.</param>
/// <param name="Property">The property that declares it.</param>
/// <param name="Kind">The shape of the reading the property holds.</param>
/// <param name="ValueType">The enumeration or number the reading is over.</param>
/// <remarks>
/// The property is kept because projection and materialization both need it, and re-deriving it from the
/// axis identity would mean the identity had to be reversible into a member name — which is one more place
/// for the two to drift.
/// </remarks>
internal sealed record DeclaredAxis(
    QualityAxis Axis,
    PropertyInfo Property,
    AxisValueShape Kind,
    Type ValueType);

/// <summary>The shape of the reading an axis property holds.</summary>
internal enum AxisValueShape
{
    /// <summary>One member of a closed set.</summary>
    Member = 0,

    /// <summary>One quantity.</summary>
    Quantity = 1,

    /// <summary>Several members of a closed set at once.</summary>
    MemberSet = 2,
}
