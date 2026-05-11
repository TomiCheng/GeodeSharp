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
    public List<CacheXmlPoolOptions> Pools { get; } = new();

    /// <summary>
    /// Top-level regions declared in the XML
    /// (<c>&lt;region&gt;</c>). Regions can nest via
    /// <see cref="CacheXmlRegionOptions.ChildRegions"/>.
    /// </summary>
    public List<CacheXmlRegionOptions> Regions { get; } = new();

    /// <summary>
    /// PDX defaults declared in the XML (<c>&lt;pdx&gt;</c>).
    /// </summary>
    public CacheXmlPdxOptions Pdx { get; } = new();

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
    public Dictionary<string, CacheXmlRegionAttributesOptions> NamedAttributes { get; } = new();
}
