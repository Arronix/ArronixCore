using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Registration;


namespace Arronix.Host.Media.Typed;

/// <summary>
/// Prices a typed media kind by deriving its model and reading the sections it carries.
/// </summary>
/// <remarks>
/// <para>
/// The registry cannot do this itself: the sections only exist after the kind's typed values have been
/// compiled into host runtime projections. So the derivation runs here and
/// the answer crosses back as capability demands, which is the one fact the admission gate needs.
/// </para>
/// <para>
/// Public because anything that wants to check a manifest against what an extension actually registers needs
/// it — the loader through DI, and the governance suite directly. A check of that claim that did not derive
/// the model would be checking a guess.
/// </para>
/// </remarks>
public sealed class MediaTypeCapabilityReader : IMediaTypeCapabilityReader, IMediaTypeBinder<IMediaTypeRuntime>
{
    /// <inheritdoc />
    /// <remarks>
    /// A typed release policy is executable capability and therefore requires the quality capability even
    /// when the legacy quality declaration is absent.
    /// </remarks>
    public IReadOnlyList<DefinitionSectionRequirement> Requirements(IMediaTypeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var media = registration.Bind(this);
        var requirements = DefinitionCapabilityRules.Requirements(media.Model).ToList();

        var declaresAModel = media.HasReleasePolicy;
        var alreadyPriced = requirements.Any(
            requirement => requirement.Capability == Capability.Quality);

        if (declaresAModel && !alreadyPriced)
        {
            requirements.Add(new DefinitionSectionRequirement(
                Capability.Quality,
                nameof(IMediaTypeRuntime.HasReleasePolicy)));
        }

        return requirements;
    }

    /// <inheritdoc />
    public IMediaTypeRuntime Bind<TItem, TTarget, TRelease, TParser>(
        MediaType<TItem, TTarget, TRelease, TParser> definition)
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>
        => MediaTypeModelFactory.Build<TItem, TTarget, TRelease, TParser>(definition);
}
