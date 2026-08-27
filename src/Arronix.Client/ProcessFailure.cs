using System.Linq;
using System.Runtime.InteropServices;

namespace Arronix.Client;

/// <summary>Which failures leave the process unsound and must never be contained by a boundary.</summary>
/// <remarks>
/// The client's copy of the platform's rule, because the client references only the universal contracts.
/// The whole chain is inspected: an exhausted heap routinely arrives wrapped.
/// </remarks>
internal static class ProcessFailure
{
    /// <summary>Determines whether a failure must propagate rather than be contained.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns><see langword="true"/> when the process is no longer sound.</returns>
    /// <remarks>
    /// Exhausted memory, exhausted stack, corrupted memory and structured native failures.
    /// <see cref="InsufficientMemoryException"/> arrives through <see cref="OutOfMemoryException"/>, and
    /// <see cref="AccessViolationException"/> is named separately because it does not derive from
    /// <see cref="SEHException"/> on this runtime. Cancellation is not here: whether it must propagate
    /// depends on whose token it belongs to, which only the caller knows.
    /// </remarks>
    internal static bool IsFatal(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return Chain(failure).Any(static inner => inner
            is OutOfMemoryException
            or StackOverflowException
            or InsufficientExecutionStackException
            or AccessViolationException
            or SEHException);
    }

    /// <summary>Walks a failure and everything it wraps, aggregates included.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>Each distinct exception in the chain.</returns>
    internal static IEnumerable<Exception> Chain(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

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
