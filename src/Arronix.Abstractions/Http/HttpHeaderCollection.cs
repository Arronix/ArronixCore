using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Arronix.Abstractions.Http;

/// <summary>
/// A case-insensitive collection of HTTP headers that preserves repeated values.
/// </summary>
/// <remarks>
/// <para>
/// Header names are compared ordinally without regard to case, as the protocol requires. Values are
/// kept as a list per name rather than joined, because joining loses information for headers that
/// legitimately repeat — <c>Set-Cookie</c> being the one that bites in practice.
/// </para>
/// <para>
/// This type is mutable and is not thread-safe. It belongs to one request or response.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Http, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class HttpHeaderCollection : IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>
{
    private readonly Dictionary<string, List<string>> _headers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new, empty instance of the <see cref="HttpHeaderCollection"/> class.
    /// </summary>
    public HttpHeaderCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpHeaderCollection"/> class from existing headers.
    /// </summary>
    /// <param name="headers">The headers to copy.</param>
    public HttpHeaderCollection(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var header in headers)
        {
            foreach (var value in header.Value)
            {
                Add(header.Key, value);
            }
        }
    }

    /// <summary>
    /// Gets the number of distinct header names present.
    /// </summary>
    public int Count => _headers.Count;

    /// <summary>
    /// Gets the header names present, in no particular order.
    /// </summary>
    public IReadOnlyCollection<string> Names => _headers.Keys;

    /// <summary>
    /// Gets or sets the <c>Content-Type</c> header, or <see langword="null"/> when it is absent.
    /// </summary>
    public string? ContentType
    {
        get => GetSingleValue("Content-Type");
        set => SetOrRemove("Content-Type", value);
    }

    /// <summary>
    /// Gets or sets the <c>Accept</c> header, or <see langword="null"/> when it is absent.
    /// </summary>
    public string? Accept
    {
        get => GetSingleValue("Accept");
        set => SetOrRemove("Accept", value);
    }

    /// <summary>
    /// Gets or sets the <c>User-Agent</c> header, or <see langword="null"/> when it is absent.
    /// </summary>
    public string? UserAgent
    {
        get => GetSingleValue("User-Agent");
        set => SetOrRemove("User-Agent", value);
    }

    /// <summary>
    /// Gets or sets the <c>Content-Length</c> header, or <see langword="null"/> when it is absent or
    /// not a number.
    /// </summary>
    public long? ContentLength
    {
        get => long.TryParse(
                GetSingleValue("Content-Length"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var length)
            ? length
            : null;
        set => SetOrRemove("Content-Length", value?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Gets the values of one header.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns>The values, in the order they were added. Empty when the header is absent.</returns>
    public IReadOnlyList<string> this[string name] => GetValues(name);

    /// <summary>
    /// Determines whether a header is present.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns><see langword="true"/> when at least one value is present.</returns>
    public bool Contains(string name) => _headers.ContainsKey(name);

    /// <summary>
    /// Gets the values of one header.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns>The values, in the order they were added. Empty when the header is absent.</returns>
    public IReadOnlyList<string> GetValues(string name) =>
        _headers.TryGetValue(name, out var values) ? values : [];

    /// <summary>
    /// Gets the single value of a header.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns>The value, or <see langword="null"/> when the header is absent.</returns>
    /// <exception cref="InvalidOperationException">The header occurs more than once.</exception>
    public string? GetSingleValue(string name)
    {
        if (!_headers.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count > 1)
        {
            throw new InvalidOperationException(
                $"Expected header '{name}' to occur once but it occurred {values.Count} times.");
        }

        return values[0];
    }

    /// <summary>
    /// Adds a value, keeping any values already present under the same name.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value to add.</param>
    public void Add(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        if (!_headers.TryGetValue(name, out var values))
        {
            values = [];
            _headers[name] = values;
        }

        values.Add(value);
    }

    /// <summary>
    /// Replaces every value of a header with one value.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value to set.</param>
    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        _headers[name] = [value];
    }

    /// <summary>
    /// Removes every value of a header.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns><see langword="true"/> when the header was present.</returns>
    public bool Remove(string name) => _headers.Remove(name);

    /// <summary>
    /// Removes every header.
    /// </summary>
    public void Clear() => _headers.Clear();

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, IReadOnlyList<string>>> GetEnumerator()
    {
        foreach (var header in _headers)
        {
            yield return new KeyValuePair<string, IReadOnlyList<string>>(header.Key, header.Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void SetOrRemove(string name, string? value)
    {
        if (value is null)
        {
            _headers.Remove(name);
        }
        else
        {
            Set(name, value);
        }
    }
}
