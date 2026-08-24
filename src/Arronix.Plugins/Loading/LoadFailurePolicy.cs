using System.Collections.Frozen;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Arronix.Plugins.Loading;

/// <summary>
/// Which failures may be contained as one package's problem, and which must stop the process.
/// </summary>
/// <remarks>
/// <para>
/// Two boundaries with different failure surfaces, so two rules. Reading and loading a file is a bounded
/// operation whose failures can be enumerated and checked, so that boundary uses a closed allowlist and
/// anything unexpected stops admission rather than being absorbed. Running a package's own code is not
/// bounded at all — an extension may throw any type it likes — so that boundary contains everything except
/// the conditions in which the process is no longer sound.
/// </para>
/// <para>
/// Both rules inspect the whole exception chain. A load failure routinely arrives wrapped: a type
/// initializer that ran out of memory surfaces as <see cref="TypeInitializationException"/>, and a rule that
/// read only the outer type would contain a process-fatal condition as an ordinary refusal.
/// </para>
/// </remarks>
internal static class LoadFailurePolicy
{
    /// <summary>
    /// The failure types the shared-contract stage and load boundary may produce and contain.
    /// </summary>
    /// <remarks>
    /// Measured against the real boundary rather than assumed: malformed images and reference assemblies
    /// raise <see cref="BadImageFormatException"/>, some byte corruptions raise
    /// <see cref="FileLoadException"/>, and a context that is unloading raises
    /// <see cref="InvalidOperationException"/>. The rest are the file-system and metadata failures staging
    /// itself can raise. A type outside this set is not a package's problem the platform knows how to
    /// contain, and admitting the rest of an installation after one would be guesswork.
    /// </remarks>
    private static readonly FrozenSet<Type> ContainableAtTheFileBoundary = new[]
    {
        typeof(BadImageFormatException),
        typeof(FileLoadException),
        typeof(FileNotFoundException),
        typeof(DirectoryNotFoundException),
        typeof(IOException),
        typeof(UnauthorizedAccessException),
        typeof(InvalidOperationException),
        typeof(NotSupportedException),
        typeof(ArgumentException),
        typeof(ArgumentNullException),
        typeof(ArgumentOutOfRangeException),
        typeof(OverflowException),
        typeof(TypeInitializationException),
        typeof(PluginIsolationException),
        typeof(SharedContractIdentityException),
    }.ToFrozenSet();

    /// <summary>
    /// Determines whether a failure leaves the process unsound and must not be absorbed anywhere.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <returns><see langword="true"/> when it must propagate.</returns>
    /// <remarks>
    /// Cancellation belongs to the caller that requested it. The rest are exhausted memory, exhausted
    /// stack, corrupted memory and structured native failures.
    /// <see cref="AccessViolationException"/> is named separately because it does not derive from
    /// <see cref="SEHException"/> on this runtime, and <see cref="InsufficientMemoryException"/> propagates
    /// through its <see cref="OutOfMemoryException"/> base.
    /// </remarks>
    public static bool IsProcessFatal(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return Chain(failure).Any(inner => inner
            is OperationCanceledException
            or OutOfMemoryException
            or StackOverflowException
            or InsufficientExecutionStackException
            or AccessViolationException
            or SEHException);
    }

    /// <summary>
    /// Determines whether a shared-contract stage or load failure may be contained as one package's refusal.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <returns><see langword="true"/> when every exception in the chain is one this boundary produces.</returns>
    public static bool IsContainableContractFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return !IsProcessFatal(failure)
            && Chain(failure).All(inner => ContainableAtTheFileBoundary.Contains(inner.GetType())
                || ContainableAtTheFileBoundary.Any(known => known.IsInstanceOfType(inner)));
    }

    /// <summary>
    /// Determines whether a failure raised by a package's own code, or by a callback it registered, may be
    /// contained.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <returns><see langword="true"/> when the caller may record it and continue.</returns>
    /// <remarks>
    /// Deliberately not an allowlist. A package's module, constructors, property getters and load-context
    /// event handlers may throw any type, and refusing to contain one the platform has not seen before would
    /// mean a novel extension bug stopped the whole installation — which is the failure containment exists to
    /// prevent.
    /// </remarks>
    public static bool IsContainablePackageFailure(Exception failure) => !IsProcessFatal(failure);

    /// <summary>Walks an exception and everything it wraps, aggregates included.</summary>
    private static IEnumerable<Exception> Chain(Exception failure)
    {
        var pending = new Stack<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(failure);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }

                continue;
            }

            if (current.InnerException is { } wrapped)
            {
                pending.Push(wrapped);
            }
        }
    }
}
