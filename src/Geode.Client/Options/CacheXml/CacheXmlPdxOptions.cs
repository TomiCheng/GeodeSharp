namespace Geode.Client.Options;

/// <summary>
/// Mirrors the <c>&lt;pdx&gt;</c> element from <c>cache.xml</c>.
/// Distinct from <see cref="PdxOptions"/> (which mirrors the
/// <c>SystemProperties</c> PDX flag) — different cppcache source
/// (<c>CacheXmlParser</c> vs <c>SystemProperties</c>).
/// </summary>
public class CacheXmlPdxOptions
{
    /// <summary>
    /// <c>ignore-unread-fields</c>. When true, fields the local schema
    /// doesn't know about are dropped on read instead of being
    /// preserved for write-back.
    /// </summary>
    public bool? IgnoreUnreadFields { get; set; }

    /// <summary>
    /// <c>read-serialized</c>. When true, PDX values stay in serialised
    /// form on read (useful for OQL-only consumers).
    /// </summary>
    public bool? ReadSerialized { get; set; }

    /// <summary>Deep clone. Nullable bools — MemberwiseClone is sufficient.</summary>
    public CacheXmlPdxOptions DeepClone() => (CacheXmlPdxOptions)MemberwiseClone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
