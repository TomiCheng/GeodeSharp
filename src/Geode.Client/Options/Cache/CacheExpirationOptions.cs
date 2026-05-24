namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>&lt;expiration-attributes&gt;</c>. Used by the four
/// expiration slots on a region (entry-/region- × idle-time/ttl).
/// </summary>
public class CacheExpirationOptions : ICloneable
{
    public CacheExpirationOptions() { }

    public CacheExpirationOptions(CacheExpirationOptions other)
    {
        Timeout = other.Timeout;
        Action = other.Action;
    }

    /// <summary><c>timeout</c> attribute (required).</summary>
    public TimeSpan Timeout { get; set; }

    /// <summary><c>action</c> attribute (optional).</summary>
    public CacheExpirationAction? Action { get; set; }

    /// <summary>Deep clone via copy constructor.</summary>
    public CacheExpirationOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
