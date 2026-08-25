using System.Reflection;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Registration;


namespace Arronix.Host.Runtime;

/// <summary>
/// Activates one extension-owned implementation through the extension contract, never through Host DI.
/// </summary>
/// <remarks>
/// The supported constructor surface is deliberately complete and tiny: an exact public
/// <c>(IPluginContext)</c> constructor is preferred, otherwise an exact public parameterless constructor is
/// used. The context is the capability boundary and locator the contract already defines; duplicating its
/// members as injectable constructor services would create a second, drifting privilege surface.
/// </remarks>
internal sealed class PluginActivationScope(IPluginContext context, PluginRegistrationLedger ledger)
{
    private readonly IPluginContext _context = context ?? throw new ArgumentNullException(nameof(context));

    private readonly PluginRegistrationLedger _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));

    /// <summary>Constructs one implementation without consulting Host dependency injection.</summary>
    /// <param name="implementationType">The type to construct.</param>
    /// <returns>The constructed object, already owned by this extension's lifetime.</returns>
    /// <remarks>
    /// The object is recorded as owned before it is handed back, so there is no instant in which a
    /// successfully constructed extension object exists and nothing is responsible for disposing it. A cast
    /// that fails, a property getter that throws, a validation that refuses — all of them happen to an
    /// object something already owns.
    /// </remarks>
    internal object CreateInstance(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);

        if (!implementationType.IsClass || implementationType.IsAbstract)
        {
            throw new InvalidOperationException(
                $"Extension implementation '{implementationType.FullName}' must be a concrete class.");
        }

        var constructor = implementationType.GetConstructor([typeof(IPluginContext)]);
        object?[] arguments = [_context];

        if (constructor is null)
        {
            constructor = implementationType.GetConstructor(Type.EmptyTypes);
            arguments = [];
        }

        if (constructor is null)
        {
            throw new InvalidOperationException(
                $"Extension implementation '{implementationType.FullName}' has no supported activation "
                + "constructor. Declare an exact public (IPluginContext) constructor or a public "
                + "parameterless constructor; the Host service provider and its services are never exposed.");
        }

        object constructed;

        try
        {
            constructed = constructor.Invoke(arguments);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            throw new InvalidOperationException(
                $"Extension implementation '{implementationType.FullName}' threw while being constructed: "
                + failure.InnerException.Message,
                failure.InnerException);
        }

        _ledger.RecordHostActivation(constructed);
        return constructed;
    }
}
