using Arronix.Abstractions.Media;
using Arronix.Plugins.Registration;

// The typed media surface is experimental; this file is the host's side of pricing one.
#pragma warning disable ARX0020

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
public sealed class MediaTypeCapabilityReader : IMediaTypeCapabilityReader, IMediaTypeBinder<MediaKindModel>
{
    /// <inheritdoc />
    public IReadOnlyList<DefinitionSectionRequirement> Requirements(IMediaTypeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return DefinitionCapabilityRules.Requirements(registration.Bind(this));
    }

    /// <inheritdoc />
    public MediaKindModel Bind<TItem, TType>()
        where TItem : IMediaItem
        where TType : IMediaType<TItem>
        => MediaTypeModelFactory.Build<TItem, TType>().Model;
}
