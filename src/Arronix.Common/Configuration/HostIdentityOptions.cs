using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Arronix.Common.Configuration;

/// <summary>
/// The identity the platform presents to the operating system, to the filesystem and to remote servers.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place a product name enters the platform. Every name the platform derives from its
/// identity — the outbound user agent, the process-id file, the log file names, the application data folder,
/// the managed service name and the text of a fatal startup failure — is a computed property of this type
/// rather than a literal somewhere in the implementation. Re-branding the platform is therefore a
/// configuration change, and a test that changes <see cref="ApplicationName"/> observes every derived name
/// change with it.
/// </para>
/// <para>
/// The identity has no default. A host that does not supply <see cref="ApplicationName"/> fails validation
/// at startup instead of silently adopting a placeholder that would then reach a remote server in a user
/// agent header or a directory name on disk.
/// </para>
/// <para>
/// The C# <c>required</c> modifier is deliberately not used: the options pipeline constructs option
/// instances through a <c>new()</c> constraint, which a type with required members cannot satisfy. The
/// obligation is expressed with <see cref="RequiredAttribute"/> instead and enforced when validation runs.
/// </para>
/// </remarks>
public sealed class HostIdentityOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Identity";

    /// <summary>
    /// Pattern an application name must match: it starts with a letter, contains only unaccented letters,
    /// digits and single interior spaces, dots, underscores or hyphens, and ends with a letter or digit.
    /// The restriction exists because the name is projected into a filesystem path, a process name and an
    /// HTTP header token, and those three grammars intersect in very little.
    /// </summary>
    private const string ApplicationNamePattern = "^[A-Za-z][A-Za-z0-9]*([ ._-][A-Za-z0-9]+)*$";

    /// <summary>
    /// Gets or sets the canonical product name, in its display casing. Every other name on this type is
    /// derived from it.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(48, MinimumLength = 2)]
    [RegularExpression(
        ApplicationNamePattern,
        ErrorMessage = "The application name must start with a letter and may contain only letters, digits and single interior spaces, dots, underscores or hyphens.")]
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an operator-chosen label distinguishing this installation from another one running the
    /// same application. It is presentation only: no path, header or process name is derived from it.
    /// Leave it empty to present the application name itself, which <see cref="DisplayName"/> does.
    /// </summary>
    [StringLength(64)]
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the host opens the operator's default browser once startup
    /// has completed.
    /// </summary>
    /// <remarks>
    /// The one member here that is a behavior rather than a name. It travels with the identity because it
    /// is the only survivor of the legacy application options block, whose remaining members were
    /// presentation concerns belonging to the web host.
    /// </remarks>
    public bool LaunchBrowser { get; set; }

    /// <summary>
    /// Gets the name to show a human: the instance name when one was configured, the application name
    /// otherwise.
    /// </summary>
    public string DisplayName => InstanceName.Length == 0 ? ApplicationName : InstanceName;

    /// <summary>
    /// Gets the lowercase, hyphen-separated ASCII token used wherever a name has to survive a filesystem,
    /// a process table and a URL unchanged.
    /// </summary>
    public string FileNameToken => ToFileNameToken(ApplicationName);

    /// <summary>
    /// Gets the name of the folder holding application data, in display casing because it is a folder an
    /// operator navigates to by hand.
    /// </summary>
    public string DataFolderName => ApplicationName;

    /// <summary>
    /// Gets the name the platform registers under with the operating system's service manager.
    /// </summary>
    public string ServiceName => ApplicationName;

    /// <summary>
    /// Gets the name of the daemon process image.
    /// </summary>
    public string ProcessName => FileNameToken;

    /// <summary>
    /// Gets the name of the console process image, which is a separate executable from the daemon.
    /// </summary>
    public string ConsoleProcessName => FileNameToken + ".console";

    /// <summary>
    /// Gets the name of the file the running process writes its process id to.
    /// </summary>
    public string PidFileName => FileNameToken + ".pid";

    /// <summary>
    /// Gets the stem shared by every log file the platform writes, so that a log file, its debug companion
    /// and its trace companion sort together in a directory listing.
    /// </summary>
    public string LogFileNameStem => FileNameToken;

    /// <summary>
    /// Gets the product token placed in the outbound user agent. Spaces are removed rather than replaced,
    /// because a space terminates a product token in an HTTP user agent.
    /// </summary>
    public string UserAgentProductToken => ToUserAgentProductToken(ApplicationName);

    /// <summary>
    /// Reduces a name to lowercase ASCII letters and digits, collapsing every other run of characters to a
    /// single hyphen.
    /// </summary>
    /// <param name="value">The name to reduce.</param>
    /// <returns>The reduced token, which is empty when the name held no letters or digits.</returns>
    private static string ToFileNameToken(string value)
    {
        var token = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                token.Append(char.ToLowerInvariant(character));
            }
            else if (token.Length > 0 && token[^1] != '-')
            {
                token.Append('-');
            }
        }

        return token.ToString().TrimEnd('-');
    }

    /// <summary>
    /// Reduces a name to the characters an HTTP product token admits, preserving casing.
    /// </summary>
    /// <param name="value">The name to reduce.</param>
    /// <returns>The reduced token, which is empty when the name held no admissible characters.</returns>
    private static string ToUserAgentProductToken(string value)
    {
        var token = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            {
                token.Append(character);
            }
        }

        return token.ToString();
    }
}
