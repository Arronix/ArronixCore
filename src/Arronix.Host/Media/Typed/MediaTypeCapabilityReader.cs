using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;
using Arronix.Plugins.Registration;

// The typed media and quality surfaces are experimental; this file is the host's side of pricing one.
#pragma warning disable ARX0013
#pragma warning disable ARX0014
#pragma warning disable ARX0020
#pragma warning disable ARX0021

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Prices a typed media kind by deriving its model and reading the sections it carries.
/// </summary>
/// <remarks>
/// <para>
/// The registry cannot do this itself: the sections only exist after the kind's configuration call has been
/// replayed against the host's builder, and that builder is host machinery. So the derivation runs here and
/// the answer crosses back as capability demands, which is the one fact the admission gate needs.
/// </para>
/// <para>
/// Public because anything that wants to check a manifest against what an extension actually registers needs
/// it — the loader through DI, and the governance suite directly. A check of that claim that did not derive
/// the model would be checking a guess.
/// </para>
/// </remarks>
public sealed class MediaTypeCapabilityReader : IMediaTypeCapabilityReader, IMediaTypeBinder<IMediaType>
{
    /// <inheritdoc />
    /// <remarks>
    /// The sections price themselves; the one demand that does not live in a section is quality, because a
    /// family that reads its files onto typed axes declares its model on the <i>structure</i> rather than in
    /// an engine-input section. Pricing it from the section alone would let a kind ship a whole quality
    /// model against a manifest that never asked for the privilege.
    /// </remarks>
    public IReadOnlyList<DefinitionSectionRequirement> Requirements(IMediaTypeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var media = registration.Bind(this);
        var requirements = DefinitionCapabilityRules.Requirements(media.Model).ToList();

        var declaresAModel = media.Shape.FormatFamilies.Any(family => family.Quality is not null);
        var alreadyPriced = requirements.Any(
            requirement => requirement.Capability == Capability.Quality);

        if (declaresAModel && !alreadyPriced)
        {
            requirements.Add(new DefinitionSectionRequirement(
                Capability.Quality,
                $"{nameof(MediaShape)}.{nameof(MediaShape.FormatFamilies)}.Quality"));
        }

        return requirements;
    }

    /// <inheritdoc />
    public IMediaType Bind<TItem, TType>()
        where TItem : IMediaItem
        where TType : IMediaType<TItem>
        => MediaTypeModelFactory.Build<TItem, TType>();
}
