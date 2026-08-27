using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Client;

/// <summary>A serialization graph is rendered only while it stays inside the platform's bounds.</summary>
/// <remarks>
/// Each graph is well-formed and coherent; its only defect is its size, so the refusal names the bound it
/// crossed rather than something it also happened to get wrong.
/// </remarks>
[TestFixture]
public sealed class ClientContractDigestBoundTests
{
    [Test]
    public void AGraphWithinTheBoundsIsRendered()
    {
        var context = new Sprawl(chain: ClientContractLimits.MaxDepth - 1);

        Assert.That(ClientContractDigest.OfSerialization(context, context.Root), Is.Not.Empty);
    }

    [Test]
    public void AGraphDeeperThanTheBoundIsRefused()
    {
        var context = new Sprawl(chain: ClientContractLimits.MaxDepth + 4);

        Assert.That(
            () => ClientContractDigest.OfSerialization(context, context.Root),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("nests deeper than"));
    }

    [Test]
    public void AGraphWiderThanTheBoundIsRefused()
    {
        var context = new Sprawl(width: ClientContractLimits.MaxNodes + 1);

        Assert.That(
            () => ClientContractDigest.OfSerialization(context, context.Root),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("describes more than"));
    }

    /// <remarks>
    /// The decisive one for the serialization budget. Every member has the same type, so a bound that counted
    /// distinct types sees two and lets the rendering iterate a million members.
    /// </remarks>
    [Test]
    public void AGraphWithMoreMembersThanTheBoundIsRefusedThoughItsTypesAreFew()
    {
        var context = new Sprawl(width: 0, chain: 0, repeat: ClientContractLimits.MaxNodes + 1);

        Assert.Multiple(() =>
        {
            Assert.That(context.DistinctTypes, Is.EqualTo(2), "the premise: a type-only bound sees two");
            Assert.That(
                () => ClientContractDigest.OfSerialization(context, context.Root),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("describes more than"));
        });
    }

    /// <remarks>
    /// The decisive one for the schema budget being a total. The field list holds one and the choice list
    /// exactly the budget, so each is individually within it and together they are one over: only a shared
    /// total refuses them. The count is charged before any entry is read, so the list throws if walked.
    /// </remarks>
    [Test]
    public void ASchemaWithMoreChoicesThanTheBoundIsRefusedWithoutReadingThem()
    {
        FieldDescriptor[] schema =
        [
            new()
            {
                FieldId = "kind",
                Name = "Kind",
                ValueKind = FieldValueKind.Enumerated,
                Choices = new WholeBudget(),
            },
        ];

        Assert.That(
            () => ClientContractDigest.OfProjection(typeof(Sprawl.Anchor), schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("describes more than"));
    }

    /// <summary>A schema entry is read once, and the value read is the value rendered.</summary>
    [Test]
    public void ASchemaEntryIsReadOnce()
    {
        var shifty = new Shifty();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => ClientContractDigest.OfProjection(typeof(Sprawl.Anchor), shifty),
                Throws.Nothing);
            Assert.That(shifty.Reads, Is.EqualTo(1), "a second read is a second answer");
        });
    }

    /// <summary>Claims exactly the whole budget, and refuses to produce any of it.</summary>
    private sealed class WholeBudget : IReadOnlyList<FacetValue>
    {
        public FacetValue this[int index] => throw new InvalidOperationException("a choice was read");

        public int Count => ClientContractLimits.MaxNodes;

        public IEnumerator<FacetValue> GetEnumerator() => throw new InvalidOperationException("choices were read");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Answers with a field once, then with null, and counts how often it was asked.</summary>
    private sealed class Shifty : IReadOnlyList<FieldDescriptor>
    {
        private static readonly FieldDescriptor Leaf = new()
        {
            FieldId = "leaf",
            Name = "leaf",
            ValueKind = FieldValueKind.Text,
        };

        internal int Reads { get; private set; }

        public int Count => 1;

        public FieldDescriptor this[int index] => ++Reads == 1 ? Leaf : null!;

        public IEnumerator<FieldDescriptor> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A root with either many distinct members or a long chain of them, and nothing else wrong.</summary>
    private sealed class Sprawl : JsonSerializerContext
    {
        private readonly Dictionary<Type, JsonTypeInfo> _byType = [];

        internal Sprawl(int width = 1, int chain = 1, int repeat = 0)
            : base(new JsonSerializerOptions(JsonSerializerDefaults.Strict)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            })
        {
            var links = Distinct().Take(width + chain).ToArray();
            Root = Describe(typeof(Sprawl.Anchor));

            // Every member the same type: two types in all, and as many members as asked for.
            for (var index = 0; index < repeat; index++)
            {
                Member(Root, typeof(Sprawl.Leaf));
            }

            // Width first: the root points at that many leaves. Then a chain, each link pointing at the next.
            for (var index = 0; index < width; index++)
            {
                Member(Root, links[index]);
            }

            var current = Root;

            for (var index = width; index < links.Length; index++)
            {
                var next = Describe(links[index]);
                Member(current, links[index]);
                current = next;
            }

            foreach (var info in _byType.Values)
            {
                info.MakeReadOnly();
            }
        }

        internal JsonTypeInfo Root { get; }

        internal int DistinctTypes => _byType.Count;

        protected override JsonSerializerOptions? GeneratedSerializerOptions => Options;

        public override JsonTypeInfo? GetTypeInfo(Type type) =>
            _byType.TryGetValue(type, out var info) ? info : null;

        private JsonTypeInfo Describe(Type type)
        {
            if (_byType.TryGetValue(type, out var existing))
            {
                return existing;
            }

            var info = JsonTypeInfo.CreateJsonTypeInfo(type, Options);
            info.CreateObject = () => Activator.CreateInstance(type)!;
            info.OriginatingResolver = this;
            _byType[type] = info;
            return info;
        }

        private void Member(JsonTypeInfo owner, Type type)
        {
            Describe(type);
            var property = owner.CreateJsonPropertyInfo(type, "m" + owner.Properties.Count);
            property.Get = static _ => null;
            owner.Properties.Add(property);
        }

        /// <summary>
        /// Distinct closed types, from triples of framework primitives.
        /// </summary>
        /// <remarks>
        /// Nesting one generic to thousands of levels would work and would make each type's rendered name
        /// grow with its position, so the cost of the case would be quadratic in what it is bounding.
        /// </remarks>
        private static IEnumerable<Type> Distinct()
        {
            Type[] arguments =
            [
                typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
                typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(bool),
                typeof(char), typeof(string), typeof(Guid), typeof(DateOnly), typeof(TimeOnly), typeof(Uri),
            ];

            foreach (var first in arguments)
            {
                foreach (var second in arguments)
                {
                    foreach (var third in arguments)
                    {
                        yield return typeof(Box<,,>).MakeGenericType(first, second, third);
                    }
                }
            }
        }

        internal sealed class Anchor;

        internal sealed class Leaf;

        private sealed class Box<T1, T2, T3>;
    }
}
