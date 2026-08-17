using System.Globalization;
using System.Text;
using System.Threading;
using System.Xml;
using Arronix.Common.Xml;

namespace Arronix.Common.Tests.Xml;

/// <summary>
/// Covers the writer whose only job is to make an XML declaration tell the truth.
/// </summary>
[TestFixture]
public class Utf8StringWriterTests
{
    [Test]
    public void Encoding_IsUtf8()
    {
        using var writer = new Utf8StringWriter();

        Assert.That(writer.Encoding.WebName, Is.EqualTo("utf-8"));
    }

    [Test]
    public void Encoding_EmitsNoByteOrderMark()
    {
        using var writer = new Utf8StringWriter();

        Assert.That(writer.Encoding.GetPreamble(), Is.Empty);
    }

    [Test]
    public void Declaration_AnnouncesUtf8()
    {
        // The framework's own string writer answers UTF-16, so a document written through it announces an
        // encoding it is not then saved in, and strict readers reject it.
        using var writer = new Utf8StringWriter();

        using (var xml = XmlWriter.Create(writer, new XmlWriterSettings { Indent = false }))
        {
            xml.WriteStartDocument();
            xml.WriteElementString("entry", "value");
            xml.WriteEndDocument();
        }

        Assert.That(writer.ToString(), Does.Contain("encoding=\"utf-8\""));
    }

    [Test]
    public void Writer_AppendsToASuppliedBuffer()
    {
        var builder = new StringBuilder("prefix:");

        using (var writer = new Utf8StringWriter(builder))
        {
            writer.Write("suffix");
        }

        Assert.That(builder.ToString(), Is.EqualTo("prefix:suffix"));
    }

    [Test]
    public void Writer_FormatsValuesInvariantlyRegardlessOfTheHostCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            using var writer = new Utf8StringWriter();
            writer.Write(1.5d);

            // Under the host culture this would be "1,5" — a decimal comma reaching a consumer expecting a
            // decimal point is a corrupt document, not a presentation difference.
            Assert.That(writer.ToString(), Is.EqualTo("1.5"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
