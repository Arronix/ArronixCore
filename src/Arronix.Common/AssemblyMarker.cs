using System.Reflection;

namespace Arronix.Common;

/// <summary>
/// Anchors a strongly-typed reference to the platform implementation assembly.
/// </summary>
/// <remarks>
/// Tooling that reflects over this assembly — dependency-shape checks, naming audits and the composition
/// root's explicit registration — needs a handle on it without taking a dependency on some arbitrary
/// implementation type that may later move or be removed.
/// </remarks>
public static class AssemblyMarker
{
    /// <summary>
    /// Gets the assembly containing the platform implementation.
    /// </summary>
    public static Assembly Assembly => typeof(AssemblyMarker).Assembly;
}
