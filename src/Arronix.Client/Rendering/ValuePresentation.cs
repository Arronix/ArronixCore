namespace Arronix.Client.Rendering;

/// <summary>
/// The handful of ways this client draws a value, once its declared shape has been read.
/// </summary>
/// <remarks>
/// Twenty declared value shapes collapse to six drawings: most values are simply text. Naming the six
/// keeps the twenty-way table a table of decisions rather than twenty blocks of near-identical markup,
/// and it is what lets a value be drawn the same way in a card, a table cell and a working surface.
/// </remarks>
public enum ValuePresentation
{
    /// <summary>Read as a line of text.</summary>
    Text = 0,

    /// <summary>Read as several lines of text, wrapped.</summary>
    Paragraph = 1,

    /// <summary>Drawn as the image it addresses.</summary>
    Image = 2,

    /// <summary>Drawn as something that can be followed.</summary>
    Address = 3,

    /// <summary>Drawn as a two-state mark.</summary>
    Flag = 4,

    /// <summary>Drawn as a proportion of a whole.</summary>
    Proportion = 5,

    /// <summary>Read as text whose exact characters matter, so it is not re-flowed.</summary>
    Verbatim = 6
}
