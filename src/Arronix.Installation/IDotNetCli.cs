namespace Arronix.Installation;

/// <summary>
/// The SDK operations the composer needs, seamed out so its staging and commit behaviour can be proved
/// against a fast, deterministic stand-in instead of a real <c>dotnet publish</c> every time.
/// </summary>
internal interface IDotNetCli
{
    /// <summary>Reports the SDK version in use, for the installation manifest.</summary>
    /// <param name="workingDirectory">The directory the version is resolved from, so global.json applies.</param>
    /// <returns>The version text, or a stated absence.</returns>
    string Version(string workingDirectory);

    /// <summary>Publishes one project into a directory, replacing whatever was there.</summary>
    /// <param name="projectFile">The project to publish.</param>
    /// <param name="destination">The directory to publish into.</param>
    /// <param name="workingDirectory">The directory the SDK is invoked from.</param>
    /// <exception cref="InstallationException">The publish failed.</exception>
    void Publish(string projectFile, string destination, string workingDirectory);
}
