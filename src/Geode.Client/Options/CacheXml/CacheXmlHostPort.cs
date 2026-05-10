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
}
