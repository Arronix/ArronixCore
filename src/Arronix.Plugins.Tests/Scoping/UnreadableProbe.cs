namespace Arronix.Plugins.Tests.Scoping;

/// <summary>
/// A failure that will not say what went wrong.
/// </summary>
/// <remarks>
/// Loaded into a collectible context of its own, so it stands for the case that matters: a failure whose
/// type is an extension's, whose members are the extension's code, and whose context must still unload.
/// </remarks>
public sealed class UnreadableProbe : Exception
{
    public override string Message => throw new InvalidOperationException("not telling");

    public override string? StackTrace => throw new InvalidOperationException("nor that");
}
