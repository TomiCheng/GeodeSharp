namespace Geode.Client.Options;

/// <summary>
/// Mirrors the cppcache <c>cache.xml</c> declarative-cache schema
/// (<c>xsds/cpp-cache-1.0.xsd</c>, root element
/// <c>&lt;client-cache&gt;</c>). Parser source:
/// <c>cppcache/src/CacheXmlParser.cpp</c>.
/// </summary>
/// <remarks>
/// CLAUDE.md cuts <c>cache.xml</c> entirely; this whole tree is on the
/// deletion shortlist and only exists so the audit can prove no
/// consumer needs it. Kept separate from the
/// <c>SystemProperties</c>-derived options (<see cref="PoolOptions"/>,
/// <see cref="PdxOptions"/>, ...) because cppcache models these as two
/// different sources (<c>SystemProperties</c> vs <c>PoolFactory</c> /
/// <c>CacheXmlCreation</c>) — collapsing them would hide that.
/// </remarks>
public class CacheXmlOptions
{
    /// <summary>
    /// Root <c>&lt;client-cache endpoints&gt;</c> attribute. Legacy
    /// inline endpoint list; default empty.
    /// </summary>
    public string Endpoints { get; set; } = string.Empty;

    /// <summary>
    /// Root <c>&lt;client-cache redundancy-level&gt;</c> attribute.
    /// Legacy HA setting; default empty.
    /// </summary>
    public string RedundancyLevel { get; set; } = string.Empty;

    /// <summary>
    /// Schema version pinned in <c>&lt;client-cache version&gt;</c>;
    /// XSD fixes this to <c>"1.0"</c>.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Named connection pools declared in the XML
    /// (<c>&lt;pool&gt;</c>). cppcache stores these in
    /// <c>PoolManager</c>, keyed by <see cref="CacheXmlPoolOptions.Name"/>.
    /// </summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public List<CacheXmlPoolOptions> Pools { get; set; } = new();

    /// <summary>
    /// Top-level regions declared in the XML
    /// (<c>&lt;region&gt;</c>). Regions can nest via
    /// <see cref="CacheXmlRegionOptions.ChildRegions"/>.
    /// </summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public List<CacheXmlRegionOptions> Regions { get; set; } = new();

    /// <summary>
    /// PDX defaults declared in the XML (<c>&lt;pdx&gt;</c>).
    /// </summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public CacheXmlPdxOptions Pdx { get; set; } = new();

    /// <summary>
    /// Reusable region-attributes templates, keyed by name. A
    /// <see cref="CacheXmlRegionOptions"/> with non-empty
    /// <see cref="CacheXmlRegionOptions.RefId"/> looks up its template
    /// here at <c>InitializeCoreAsync</c> time; the template's values
    /// supply defaults that the region's inline
    /// <see cref="CacheXmlRegionOptions.Attributes"/> can override.
    /// Mirrors cppcache <c>&lt;region-attributes id="..."&gt;</c> →
    /// <c>&lt;region refid="..."&gt;</c> template inheritance
    /// (<c>cppcache/src/CacheXmlParser.cpp</c> <c>namedRegions_</c>).
    /// </summary>
    /// <remarks>
    /// Single-level only — a template's own <c>RefId</c> is not
    /// followed (no chained inheritance). Inner
    /// <c>&lt;region-attributes refid="..."&gt;</c> is also unsupported
    /// today; only the outer <see cref="CacheXmlRegionOptions.RefId"/>
    /// triggers resolution.
    /// </remarks>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public Dictionary<string, CacheXmlRegionAttributesOptions> NamedAttributes { get; set; } = new();

    /// <summary>Deep clone. Lists / dict / nested <see cref="CacheXmlPdxOptions"/> are deep-copied.</summary>
    public CacheXmlOptions DeepClone()
    {
        var clone = (CacheXmlOptions)MemberwiseClone();
        clone.Pools = Pools.Select(p => p.DeepClone()).ToList();
        clone.Regions = Regions.Select(r => r.DeepClone()).ToList();
        clone.Pdx = Pdx.DeepClone();
        clone.NamedAttributes = NamedAttributes.ToDictionary(kv => kv.Key, kv => kv.Value.DeepClone());
        return clone;
    }

    /// <summary>
    /// Validate. Rules migrated from <c>GeodeClientOptionsValidator</c>:
    /// <see cref="Pools"/> must contain at least one entry; each region's
    /// <see cref="CacheXmlRegionOptions.RefId"/> must reference a key in
    /// <see cref="NamedAttributes"/>. Recurses into pools and regions.
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        if (Pools.Count == 0)
            yield return $"{prefix}.Pools must contain at least one pool.";

        for (var i = 0; i < Pools.Count; i++)
            foreach (var f in Pools[i].Validate($"{prefix}.Pools[{i}]"))
                yield return f;

        for (var i = 0; i < Regions.Count; i++)
        {
            foreach (var f in Regions[i].Validate($"{prefix}.Regions[{i}]")) yield return f;

            // Cross-ref check needs NamedAttributes — done here, not in
            // CacheXmlRegionOptions.Validate (which doesn't see siblings).
            // Mirrors cppcache CacheXmlParser.cpp:777-786.
            var refId = Regions[i].RefId;
            if (!string.IsNullOrEmpty(refId) && !NamedAttributes.ContainsKey(refId))
                yield return $"{prefix}.Regions[{i}].RefId='{refId}' does not match any key in {prefix}.NamedAttributes.";
        }
    }
}
