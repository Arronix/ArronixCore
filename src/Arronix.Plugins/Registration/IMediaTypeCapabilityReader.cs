using Arronix.Abstractions.Media;


namespace Arronix.Plugins.Registration;

/// <summary>
/// Prices a typed media kind's registration in capabilities: which ones its declaration demands, and
/// therefore which ones it accounts for.
/// </summary>
/// <remarks>
/// <para>
/// The bidirectional capability check needs the answer at the moment of registration — the reverse half
/// refuses a section whose capability was never granted, and the forward half quarantines a manifest that
/// declared a capability nothing used, and the loader runs the forward half before the host has admitted
/// anything. But the answer is only readable by compiling the kind's typed definition values, and that
/// compilation is host machinery. This interface is that one fact crossing the boundary, and nothing else.
/// </para>
/// <para>
/// Deliberately not defaulted. A registry built without a reader refuses a typed media kind outright rather
/// than pricing it at the media-kind capability alone, because a check that silently narrows is worse than
/// one that is absent: it reads as enforcement while granting whatever it failed to look at.
/// </para>
/// </remarks>
public interface IMediaTypeCapabilityReader
{
    /// <summary>
    /// Gets every capability a typed kind's declaration demands, each paired with the section demanding it.
    /// </summary>
    /// <param name="registration">The captured registration.</param>
    /// <returns>The demands, the registration gate itself first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is <see langword="null"/>.</exception>
    IReadOnlyList<DefinitionSectionRequirement> Requirements(IMediaTypeRegistration registration);
}
