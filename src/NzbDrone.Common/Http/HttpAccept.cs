namespace NzbDrone.Common.Http;

public sealed class HttpAccept(string accept)
{
    public static readonly HttpAccept Rss = new HttpAccept("application/rss+xml, text/rss+xml, application/xml, text/xml");
    public static readonly HttpAccept Json = new HttpAccept("application/json");
    public static readonly HttpAccept JsonCharset = new HttpAccept("application/json; charset=utf-8");
    public static readonly HttpAccept Html = new HttpAccept("text/html");

    public string Value { get; private set; } = accept;

    public override string ToString()
    {
        return Value;
    }
}
