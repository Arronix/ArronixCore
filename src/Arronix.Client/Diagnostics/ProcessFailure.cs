using System.Linq;
using System.Runtime.InteropServices;

namespace Arronix.Client.Diagnostics;

/// <summary>Which failures leave the process unsound and must never be contained by a boundary.</summary>
/// <remarks>
/// The rule `Arronix.Common.Lifetimes.ProcessFailure` states, restated because the Client references the
/// universal contract assembly and nothing else.
/// </remarks>
internal static class ProcessFailure
{
    /// <summary>Determines whether a failure must propagate rather than be contained.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns><see langword="true"/> when the process is no longer sound.</returns>
    /// <remarks>
    /// The whole chain is read: an exhausted heap arrives wrapped. <see cref="AccessViolationException"/>
    /// is named separately because it does not derive from <see cref="SEHException"/>. Cancellation is not
    /// here — whose token it belongs to is a question only the caller can answer.
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
