using System.Globalization;
using System.IO;
using System.Text;

namespace Arronix.Common.Xml;

/// <summary>
/// A string writer that reports its encoding as UTF-8.
/// </summary>
/// <remarks>
/// <para>
/// The one workaround in this area that the framework still has no replacement for. An XML writer asks the
/// text writer it was given what encoding it uses and puts that answer in the declaration; the framework's
/// own string writer answers UTF-16 and offers no way to change it, because in memory that is genuinely what
/// a string is. The result is a document announcing <c>encoding="utf-16"</c> that is then written to a file
/// or a request body as UTF-8, which every strict reader rejects.
/// </para>
/// <para>
/// Values are formatted with the invariant culture. Numbers and dates written into a document must not
/// depend on the locale of the machine that produced it — a decimal comma reaching a consumer expecting a
/// decimal point is a corrupt document, not a presentation difference.
/// </para>
/// </remarks>
public sealed class Utf8StringWriter : StringWriter
{
    /// <summary>
    /// UTF-8 with no byte order mark, matching what the framework's XML writer emits by default.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8StringWriter"/> class writing to a new buffer.
    /// </summary>
    public Utf8StringWriter()
        : base(CultureInfo.InvariantCulture)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Utf8StringWriter"/> class appending to an existing
    /// buffer.
    /// </summary>
    /// <param name="builder">The buffer written to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public Utf8StringWriter(StringBuilder builder)
        : base(builder, CultureInfo.InvariantCulture)
    {
    }

    /// <summary>
    /// Gets the encoding the writer reports to anything that asks, which is UTF-8 without a byte order mark.
    /// </summary>
    public override Encoding Encoding => Utf8NoBom;
}
