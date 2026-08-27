using System.Linq;
using System.Text.Json.Serialization;
using Arronix.Abstractions.Client;

namespace Arronix.Plugin.Movies.Tests.ClientContract;

/// <summary>
/// The declared hashes, checked against what the running metadata actually is.
/// </summary>
/// <remarks>
/// A hash computed from the model that produced the value it is checking proves nothing. The declaration
/// carries literals its generator computed from a compile-time model of the framework's serializer; these
/// cases recompute the same canonical rendering from the live <c>JsonTypeInfo</c> graph and the live
/// schema, and require them to be equal. A model that drifted from the framework fails here rather than in
/// a browser.
/// </remarks>
[TestFixture]
public sealed class ClientContractDigestTests
{
    private static ClientContractEntryPointAttribute Declaration =>
        MovieClientContractTests.Declaration;

    [Test]
    public void TheDeclaredMetadataHashIsTheHashOfTheRunningSerializationGraph()
    {
        Assert.That(
            ClientContractDigest.OfSerialization(Declaration.SerializationContext, Declaration.EntityTypeInfo),
            Is.EqualTo(Declaration.GeneratedMetadataHash),
            ClientContractDigest.RenderSerialization(Declaration.SerializationContext, Declaration.EntityTypeInfo));
    }

    [Test]
    public void TheDeclaredProjectionHashIsTheHashOfTheRunningSchema()
    {
        Assert.That(
            ClientContractDigest.OfProjection(Declaration.EntityType, Declaration.Schema),
            Is.EqualTo(Declaration.ProjectionSchemaHash),
            ClientContractDigest.RenderProjection(Declaration.EntityType, Declaration.Schema));
    }

    [Test]
    public void TheTwoHashesDescribeDifferentThings()
    {
        Assert.That(Declaration.GeneratedMetadataHash, Is.Not.EqualTo(Declaration.ProjectionSchemaHash));
    }

    /// <remarks>
    /// The rendering, not just its hash, so a failure elsewhere can be read. Each of these is a fact a
    /// payload's meaning depends on, and each would move the hash if it moved.
    /// </remarks>
    [Test]
    public void TheSerializationRenderingCarriesTheStrictOptionsAndTheRealWireShape()
    {
        var rendering = ClientContractDigest.RenderSerialization(Declaration.SerializationContext, Declaration.EntityTypeInfo);

        Assert.Multiple(() =>
        {
            Assert.That(rendering, Does.StartWith(
                "options|caseInsensitive=false|unmapped=Disallow|duplicates=false|respectNullable=true"
                + "|respectRequiredCtorParameters=true|numbers=Strict|comments=Disallow|trailingCommas=false"
                + "|ignoreCondition=Never|includeFields=false\n"));

            Assert.That(rendering, Does.Contain("type=Arronix.Media.Movies.Movie|kind=Object\n"));

            // Wire name, declared type, direction, requiredness and nullability.
            Assert.That(rendering, Does.Contain(
                "  member=title|System.String|read=true|write=true|required=true"
                + "|getNullable=false|setNullable=false\n"));

            // A member the constructor writes and no setter reads.
            Assert.That(rendering, Does.Contain(
                "  member=minimum|System.Decimal|read=false|write=true|required=true"
                + "|getNullable=false|setNullable=false\n"));

            // A derived member reaches the wire in neither direction.
            Assert.That(rendering, Does.Contain("  member=status|ignored\n"));
            Assert.That(rendering, Does.Contain("  member=normalizedValue|ignored\n"));

            // Generic arguments are spelled without assembly qualification, so a framework patch that
            // changed nothing about a payload does not move the hash.
            Assert.That(rendering, Does.Contain(
                "type=System.Collections.Generic.IReadOnlyList<Arronix.Abstractions.Media.Rating>"
                + "|kind=Enumerable|element=Arronix.Abstractions.Media.Rating\n"));
        });
    }

    [Test]
    public void TheProjectionRenderingCoversEveryDeclaredFieldAndItsComponents()
    {
        var rendering = ClientContractDigest.RenderProjection(Declaration.EntityType, Declaration.Schema);

        Assert.Multiple(() =>
        {
            Assert.That(rendering, Does.StartWith("entity=Arronix.Media.Movies.Movie\n"));
            Assert.That(
                rendering.Split('\n').Count(line => line.StartsWith("  field=", StringComparison.Ordinal)),
                Is.EqualTo(Declaration.Schema.Count));
            Assert.That(rendering, Does.Contain("    field=memberCount|"), "a composite's components are covered");
        });
    }

    /// <remarks>
    /// The digest has to move when the wire moves, or it is decoration. Two schemas differing in one
    /// field's shape must not hash alike.
    /// </remarks>
    [Test]
    public void ADifferentSchemaHashesDifferently()
    {
        var schema = Declaration.Schema.ToArray();
        var altered = schema.ToArray();
        altered[0] = altered[0] with { Multivalued = !altered[0].Multivalued };

        Assert.That(
            ClientContractDigest.OfProjection(Declaration.EntityType, altered),
            Is.Not.EqualTo(ClientContractDigest.OfProjection(Declaration.EntityType, schema)));
    }

    /// <remarks>
    /// Resolution goes through the contract's own context and never through its options. The options are
    /// the reason: they answer for this graph or they throw, so a rendering that reached for them would
    /// either describe the same thing or fail in a place that names the serializer rather than the
    /// contract. Asking the context returns nothing, which is an answer a caller can act on.
    /// </remarks>
    [Test]
    public void ATypeOutsideTheGeneratedGraphHasNoMetadataAndNoFallback()
    {
        var context = Declaration.SerializationContext;

        Assert.Multiple(() =>
        {
            Assert.That(context.GetTypeInfo(typeof(Guid)), Is.Null);
            Assert.That(
                () => context.Options.GetTypeInfo(typeof(Guid)),
                Throws.InstanceOf<NotSupportedException>(),
                "the options carry this resolver and no reflecting one behind it");
        });
    }

    /// <remarks>
    /// A root from somewhere else describes somewhere else. Rendering it under this contract's name would
    /// produce a hash for a graph this contract does not have.
    /// </remarks>
    [Test]
    public void ARootFromAnotherContextIsRefused()
    {
        Assert.That(
            () => ClientContractDigest.RenderSerialization(
                Declaration.SerializationContext,
                Serialization.WireBehaviorContext.Default.WireChild!),
            Throws.ArgumentException);
    }

    [Test]
    public void TheDeclarationExposesTheRealSerializationContext()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Declaration.SerializationContext, Is.Not.Null);
            Assert.That(Declaration.EntityTypeInfo.Type, Is.SameAs(Declaration.EntityType));
            Assert.That(Declaration.EntityTypeInfo.Options, Is.SameAs(Declaration.SerializationContext.Options));
            Assert.That(Declaration.EntityTypeInfo.Options.UnmappedMemberHandling,
                Is.EqualTo(JsonUnmappedMemberHandling.Disallow));
        });
    }
}
