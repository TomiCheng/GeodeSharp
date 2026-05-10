namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>region-type</c>. Regions can nest via
/// <see cref="ChildRegions"/>.
/// </summary>
public class CacheXmlRegionOptions
{
    /// <summary><c>name</c> attribute (required).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>refid</c> attribute (optional) — copy attributes
    /// from a previously-defined region.</summary>
    public string RefId { get; set; } = string.Empty;

    /// <summary><c>&lt;region-attributes&gt;</c> child.</summary>
    public CacheXmlRegionAttributesOptions Attributes { get; } = new();

    /// <summary>Nested <c>&lt;region&gt;</c> children.</summary>
    public List<CacheXmlRegionOptions> ChildRegions { get; } = new();
}
