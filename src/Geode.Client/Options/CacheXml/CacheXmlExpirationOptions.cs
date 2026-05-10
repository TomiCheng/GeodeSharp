namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>&lt;expiration-attributes&gt;</c>. Used by the four
/// expiration slots on a region (entry-/region- × idle-time/ttl).
/// </summary>
public class CacheXmlExpirationOptions
{
    /// <summary><c>timeout</c> attribute (required).</summary>
    public TimeSpan Timeout { get; set; }

    /// <summary><c>action</c> attribute (optional).</summary>
    public CacheXmlExpirationAction? Action { get; set; }
}
