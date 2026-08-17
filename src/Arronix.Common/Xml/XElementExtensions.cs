using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace Arronix.Common.Xml;

/// <summary>
/// Namespace-agnostic traversal of XML documents.
/// </summary>
/// <remarks>
/// Feeds in the wild declare, misdeclare and omit XML namespaces freely, and the same publisher will change
/// which it does between releases. Matching on the local name alone is therefore not sloppiness but the only
/// approach that survives contact with real documents; a reader bound to a fully qualified name returns
/// nothing the day a publisher moves a namespace, and returns it silently.
/// </remarks>
public static class XElementExtensions
{
    /// <summary>
    /// Finds all descendant elements whose local name matches, whatever namespace they are in.
    /// </summary>
    /// <param name="container">The element or document to search.</param>
    /// <param name="localName">The local name to match, compared without regard to case.</param>
    /// <returns>The matching descendants, in document order.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="container"/> or <paramref name="localName"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Names are compared ordinally. The comparison this replaces used the invariant culture's collation
    /// rules, which is the wrong tool for a markup identifier: collation exists to order human-readable text
    /// for a reader, and applying it to element names makes matching depend on rules that have nothing to do
    /// with the document.
    /// </remarks>
    public static IEnumerable<XElement> FindDescendants(this XContainer container, string localName)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(localName);

        return container
            .Descendants()
            .Where(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads an attribute's value if the attribute is present.
    /// </summary>
    /// <param name="element">The element to read from.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">
    /// Receives the attribute's value when the method returns <see langword="true"/>, and
    /// <see langword="null"/> otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the attribute is present; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="element"/> or <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The out parameter is annotated so the compiler knows it is only meaningful on success. Without the
    /// annotation — which the implementation this replaces lacked, while still assigning
    /// <see langword="null"/> on the failure path — every caller had to either re-check a value the compiler
    /// believed could not be null, or suppress the warning and hope.
    /// </remarks>
    public static bool TryGetAttributeValue(
        this XElement element,
        string name,
        [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(name);

        var attribute = element.Attribute(name);

        if (attribute is null)
        {
            value = null;
            return false;
        }

        value = attribute.Value;
        return true;
    }
}
