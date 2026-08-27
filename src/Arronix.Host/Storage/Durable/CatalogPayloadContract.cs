using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Media;
using Arronix.Common.Lifetimes;
using Arronix.Host.Media;

namespace Arronix.Host.Storage.Durable;

/// <summary>
/// Writes and reads one media type's item through the bridge that media type's own build generated.
/// </summary>
/// <remarks>
/// <para>
/// The host does not know what a movie is and must not learn. What it holds is the compiled bridge the
/// media type's runtime carries: the exact CLR type, the generated reader and writer, and a hash over the
/// member graph that reader accepts. Persisting through it is what lets a durable record hold the complete
/// typed item — including the release timeline whose milestones the common contract deliberately refuses to
/// flatten — without a single media fact appearing in a host table.
/// </para>
/// <para>
/// The bridge arrives on the runtime, having been resolved by the media type's own compiler. Nothing here
/// reads assembly attributes, resolves a type by name or constructs anything: a host that had to go looking
/// for a codec would be a host that could find the wrong one.
/// </para>
/// </remarks>
internal sealed class CatalogPayloadContract
{
    private readonly ICompiledItemCodec _codec;

    private CatalogPayloadContract(ICompiledItemCodec codec) => _codec = codec;

    /// <summary>Gets the exact type this contract reads and constructs.</summary>
    internal Type EntityType => _codec.ItemType;

    /// <summary>Gets the writing build's hash over the member graph its reader accepts.</summary>
    internal string MetadataHash => _codec.MetadataHash;

    /// <summary>
    /// Takes the bridge one media runtime carries.
    /// </summary>
    /// <param name="runtime">The kind's closed typed runtime.</param>
    /// <returns>The contract.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArronixException">
    /// The runtime carries no bridge, so there is no generated serialization through which a record could be
    /// written and read back. Refused rather than substituted: a host-invented serialization would be a
    /// second answer about the item's shape.
    /// </exception>
    internal static CatalogPayloadContract For(IMediaTypeRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (runtime is IItemCodecBinding { ItemCodec: { } codec })
        {
            return new CatalogPayloadContract(codec);
        }

        throw new ArronixException(
            CoreErrorCode.CatalogRecordUnreadable,
            $"Media kind '{runtime.Kind}' carries no compiled bridge for '{runtime.ItemType.FullName}', so "
            + "the platform has no generated serialization through which to store an item of that type and "
            + "read it back. An item type that derives from the common item and declares a serialization "
            + "context has one generated into the assembly that publishes it.");
    }

    /// <summary>Writes one typed item into the bytes this contract's own reader accepts.</summary>
    /// <param name="item">The item, which must be of <see cref="EntityType"/>.</param>
    /// <returns>The payload.</returns>
    internal byte[] Write(IMediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _codec.Write(item);
    }

    /// <summary>
    /// Reads one payload back into the exact typed item it holds.
    /// </summary>
    /// <param name="payload">The stored bytes.</param>
    /// <param name="writtenBy">The metadata hash recorded when they were written.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ArronixException">
    /// The payload was written by a build whose reader accepts a different member graph, or it is not a
    /// readable item. Both leave the record intact and unread rather than producing a partial item.
    /// </exception>
    /// <remarks>
    /// The hash is checked before the bytes reach the reader. A build that would read a payload differently
    /// differs here whatever its version says, so this is the check that keeps a stored record from being
    /// quietly reinterpreted by a later contract.
    /// </remarks>
    internal IMediaItem Read(byte[] payload, string writtenBy)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!string.Equals(writtenBy, MetadataHash, StringComparison.Ordinal))
        {
            throw new ArronixException(
                CoreErrorCode.CatalogRecordUnreadable,
                $"A stored record of '{EntityType.FullName}' was written through metadata '{writtenBy}', and "
                + $"the installed build reads '{MetadataHash}'. The record is kept and refused rather than "
                + "read through a reader that accepts a different member graph.");
        }

        try
        {
            // The bridge constructs its own exact type and hands it back as the item contract every item
            // satisfies, so nothing here has to ask what came back what it is.
            return _codec.Read(payload);
        }
        catch (Exception failure) when (failure is not OperationCanceledException && !ProcessFailure.IsFatal(failure))
        {
            // The reader is the media package's own generated code, so it may throw anything. Whatever it
            // was, the record is intact and unread: one visible failure rather than a partly built item.
            throw new ArronixException(
                CoreErrorCode.CatalogRecordUnreadable,
                $"A stored record of '{EntityType.FullName}' could not be read back into one.",
                failure);
        }
    }
}
