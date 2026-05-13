namespace Geode.Client.Options;

/// <summary>
/// <c>host-port-type</c> in the XSD — used by
/// <c>&lt;locator&gt;</c> and <c>&lt;server&gt;</c> entries inside a
/// <c>&lt;pool&gt;</c>.
/// </summary>
public class CacheXmlHostPort
{
    /// <summary><c>host</c> attribute (required).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary><c>port</c> attribute (required, 0–65535).</summary>
    public int Port { get; set; }

    /// <summary>
    /// Deep clone. Leaf type — only primitives + string, so
    /// <see cref="object.MemberwiseClone"/> is sufficient.
    /// </summary>
    public CacheXmlHostPort DeepClone() => (CacheXmlHostPort)MemberwiseClone();

    /// <summary>
    /// Validate this entry. Failures are returned as path-prefixed
    /// strings (the caller supplies the prefix, e.g.
    /// <c>"GeodeClientOptions.CacheXml.Pools[0].Locators[2]"</c>).
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        if (string.IsNullOrWhiteSpace(Host))
            yield return $"{prefix}.Host must not be null, empty, or whitespace.";

        // Port = 0 is rejected (cppcache convention; matches existing validator).
        if (Port is < 1 or > 65535)
            yield return $"{prefix}.Port must be in the range [1, 65535] (got {Port}).";
    }
}
