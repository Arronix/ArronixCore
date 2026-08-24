namespace Arronix.Plugins.Loading;

/// <summary>
/// How far an extension got through the load pipeline.
/// </summary>
/// <remarks>
    /// The states are a straight line with failure and teardown exits. An extension is never partially
/// activated: it reaches <see cref="Active"/> with everything committed, reaches <see cref="Stopped"/> only
/// after all of it is withdrawn, or is quarantined with nothing committed. Half-registered extensions are
/// the failure mode that makes an operator distrust the whole host, and there is no state here that can
/// represent one.
/// </remarks>
public enum PluginState
{
    /// <summary>A declaration file was found.</summary>
    Discovered = 0,

    /// <summary>The declaration is well-formed, compatible and admissible on reference grounds.</summary>
    Validated = 1,

    /// <summary>The entry assembly is loaded and its single module has been constructed.</summary>
    Loaded = 2,

    /// <summary>The module has registered everything it contributes and both capability checks passed.</summary>
    Registered = 3,

    /// <summary>The registrations are committed and the extension is serving.</summary>
    Active = 4,

    /// <summary>The extension failed a step and contributes nothing.</summary>
    Quarantined = 5,

    /// <summary>The extension was active and Host teardown withdrew all of its published contributions.</summary>
    Stopped = 6
}
