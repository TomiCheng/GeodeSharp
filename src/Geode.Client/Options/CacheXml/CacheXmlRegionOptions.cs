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
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public CacheXmlRegionAttributesOptions Attributes { get; set; } = new();

    /// <summary>Nested <c>&lt;region&gt;</c> children.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public List<CacheXmlRegionOptions> ChildRegions { get; set; } = new();

    /// <summary>Deep clone. Recurses into <see cref="Attributes"/> and each child region.</summary>
    public CacheXmlRegionOptions DeepClone()
    {
        var clone = (CacheXmlRegionOptions)MemberwiseClone();
        clone.Attributes = Attributes.DeepClone();
        clone.ChildRegions = ChildRegions.Select(r => r.DeepClone()).ToList();
        return clone;
    }

    /// <summary>
    /// Validate. Rule migrated from <c>GeodeClientOptionsValidator</c>:
    /// <see cref="Name"/> non-empty. RefId cross-reference is checked at
    /// <see cref="CacheXmlOptions.Validate"/> (needs sibling
    /// <c>NamedAttributes</c> context). Recurses into <see cref="Attributes"/>
    /// and each child region.
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return $"{prefix}.Name must not be null, empty, or whitespace.";

        foreach (var f in Attributes.Validate($"{prefix}.Attributes")) yield return f;

        for (var i = 0; i < ChildRegions.Count; i++)
            foreach (var f in ChildRegions[i].Validate($"{prefix}.ChildRegions[{i}]"))
                yield return f;
    }
}
