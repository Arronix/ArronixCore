namespace Arronix.Client.Contracts;

/// <summary>Which step of a contract transaction contained a failure.</summary>
/// <remarks>Zero is unnamed: a default value describes no step.</remarks>
internal enum ContractFailureStage
{
    /// <summary>Reading the installation.</summary>
    Load = 1,

    /// <summary>Shedding the stored bytes the installation just read no longer names.</summary>
    Sweep = 2,

    /// <summary>A subscriber refusing the view's change signal.</summary>
    Changed = 3
}

/// <summary>One ordinary failure this client contained.</summary>
/// <param name="Stage">The step that failed.</param>
/// <param name="Message">What the failure said.</param>
/// <remarks>Steps are independent, so failures are kept as values in occurrence order, never one slot.</remarks>
internal sealed record ContractFailure(ContractFailureStage Stage, string Message);
