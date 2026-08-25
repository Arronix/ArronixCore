using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Arronix.Abstractions.Events;

namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// A base an emitted event derives from, so an emitted assembly needs no members of its own.
/// </summary>
/// <remarks>
/// It lives in this assembly, which the default context loads, so an emitted event's own assembly is the
/// only thing that varies between fixtures — which is exactly what ownership is decided from.
/// </remarks>
public record EmittedEventBase : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;

    /// <inheritdoc />
    public string? CorrelationId => null;
}

/// <summary>
/// Writes a real assembly declaring one event type, so ownership can be decided about a genuine file.
/// </summary>
internal static class EmittedEvent
{
    /// <summary>The type name every emitted event assembly exposes.</summary>
    public const string TypeName = "Emitted.Event.Fact";

    /// <summary>Writes an assembly declaring one event type.</summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="assemblyName">The assembly name, which is also its file name.</param>
    /// <returns>The full path of the written assembly.</returns>
    public static string Write(string folder, string assemblyName)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly);

        builder.DefineDynamicModule(assemblyName)
            .DefineType(
                TypeName,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                typeof(EmittedEventBase))
            .CreateType();

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, assemblyName + ".dll");
        builder.Save(path);
        return path;
    }
}
