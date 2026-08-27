using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Client;

namespace Arronix.Plugin.Movies.Tests.Serialization;

/// <summary>
/// A shape the movie graph cannot exercise: constructor parameters and required members on one type.
/// </summary>
/// <remarks>
/// The generator runs over this test assembly for exactly this reason, so the hash it computed while the
/// assembly compiled can be checked against the metadata the framework actually produced, in one place.
/// </remarks>
[TestFixture]
public sealed class MixedShapeContractTests
{
    private static ClientContractEntryPointAttribute Declaration { get; } =
        typeof(MixedItem).Assembly
            .GetCustomAttributes<ClientContractEntryPointAttribute>()
            .Single(candidate => candidate.EntityType == typeof(MixedItem));

    /// <remarks>
    /// A required member's parameter continues after the constructor's own, rather than starting again at
    /// zero: measured here, and the reason the compile-time model seeds its count from the constructor.
    /// </remarks>
    [Test]
    public void ARequiredMembersParameterContinuesAfterTheConstructorParameters()
    {
        var facet = Declaration.SerializationContext.GetTypeInfo(typeof(MixedFacet))!;

        string Position(string member) =>
            facet.Properties.Single(property => property.Name == member).AssociatedParameter is { } parameter
                ? $"{parameter.Position}:{parameter.IsMemberInitializer}"
                : "none";

        Assert.Multiple(() =>
        {
            Assert.That(Position("first"), Is.EqualTo("0:False"));
            Assert.That(Position("second"), Is.EqualTo("1:False"));
            Assert.That(Position("third"), Is.EqualTo("2:True"));
            Assert.That(Position("fourth"), Is.EqualTo("3:True"));
        });
    }

    [Test]
    public void TheDeclaredMetadataHashIsTheHashOfTheRunningGraph()
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
            Is.EqualTo(Declaration.ProjectionSchemaHash));
    }

    /// <remarks>
    /// The positions and the default reach the rendering, so a change to either moves the hash.
    /// </remarks>
    [Test]
    public void TheRenderingCarriesEveryParameterPositionAndTheDefault()
    {
        var rendering = ClientContractDigest.RenderSerialization(
            Declaration.SerializationContext,
            Declaration.EntityTypeInfo);

        Assert.Multiple(() =>
        {
            Assert.That(rendering, Does.Contain("|parameter=0|5:first|13:System.String|memberInitializer=false"));
            Assert.That(rendering, Does.Contain("|parameter=1|6:second|12:System.Int32|memberInitializer=false"
                + "|nullable=false|default=9\n"));
            Assert.That(rendering, Does.Contain("|parameter=2|5:Third|13:System.String|memberInitializer=true"));
            Assert.That(rendering, Does.Contain("|parameter=3|6:Fourth|12:System.Int32|memberInitializer=true"));
        });
    }
}
