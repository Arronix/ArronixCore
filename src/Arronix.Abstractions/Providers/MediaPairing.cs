using System.ComponentModel;

namespace Arronix.Abstractions.Providers;

/// <summary>Marks a provider contract that closes <see cref="ICataloger{TItem}"/> over a media item type.</summary>
/// <remarks>
/// <para>
/// Host binding SPI, not authoring vocabulary. It exists so that a cataloger registration can name its
/// family in a generic constraint, which is what turns "this is not a cataloger" into a compiler error at
/// the registration call site instead of a refusal after admission.
/// </para>
/// <para>
/// It carries no values, and that is deliberate. Any interface can be implemented directly, so a value this
/// marker carried would be a claim the platform could not check — a type could implement the marker without
/// implementing any closed cataloger contract and still name one. The pairing is therefore read from the
/// closed contracts the implementation actually implements, once, when the registration is built.
/// </para>
/// <para>
/// Separate from <see cref="IClosedCurator"/> so the constraint is exact in both directions: a curator
/// cannot satisfy a cataloger registration, and a cataloger cannot satisfy a curator's.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IClosedCataloger : ICataloger;

/// <summary>Marks a provider contract that closes <see cref="ICurator{TItem}"/> over a media item type.</summary>
/// <remarks>
/// The curator half of the same marker. See <see cref="IClosedCataloger"/> for why it carries nothing.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IClosedCurator : IProvider;
