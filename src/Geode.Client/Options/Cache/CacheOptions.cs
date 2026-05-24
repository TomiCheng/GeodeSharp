namespace Geode.Client.Options;

/// <summary>Declarative cache configuration (pools, regions, PDX) — the per-cache half of the options tree.</summary>
public class CacheOptions : ICloneable
{
    public CacheOptions() { }

    public CacheOptions(CacheOptions other)
    {
        Endpoints = other.Endpoints.Select(e => e.Clone()).ToList();
        RedundancyLevel = other.RedundancyLevel;
        Version = other.Version;
        Pools = other.Pools.Select(p => p.Clone()).ToList();
        Regions = other.Regions.Select(r => r.Clone()).ToList();
        Pdx = other.Pdx.Clone();
        NamedAttributes = other.NamedAttributes.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
    }

    /// <summary>
    /// Inline endpoint list; when non-empty, treated as a synthesized
    /// default pool's <see cref="CachePoolOptions.Servers"/>.
    /// </summary>
    public List<CacheHostPortOptions> Endpoints { get; set; } = [];

    /// <summary>Subscription redundancy level; default empty.</summary>
    public string RedundancyLevel { get; set; } = string.Empty;

    /// <summary>Schema version; pinned to <c>"1.0"</c>.</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Named connection pools, keyed by <see cref="CachePoolOptions.Name"/>.</summary>
    public List<CachePoolOptions> Pools { get; set; } = new();

    /// <summary>Top-level regions; can nest via <see cref="CacheRegionOptions.ChildRegions"/>.</summary>
    public List<CacheRegionOptions> Regions { get; set; } = new();

    /// <summary>PDX defaults.</summary>
    public CachePdxOptions Pdx { get; set; } = new();

    /// <summary>Reusable region-attributes templates referenced by <see cref="CacheRegionOptions.RefId"/>.</summary>
    public Dictionary<string, CacheRegionAttributesOptions> NamedAttributes { get; set; } = new();

    /// <summary>Deep clone via copy constructor.</summary>
    public CacheOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate the tree: exactly one of <see cref="Endpoints"/> / <see cref="Pools"/> must be set; region <c>RefId</c>s must resolve to <see cref="NamedAttributes"/>; recurses into entries.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        var hasEndpoints = Endpoints.Count > 0;
        var hasPools = Pools.Count > 0;
        if (!hasEndpoints && !hasPools)
            yield return $"{prefix} must set either Endpoints or Pools.";
        else if (hasEndpoints && hasPools)
            yield return $"{prefix}.Endpoints and {prefix}.Pools are mutually exclusive — set one or the other.";

        for (var i = 0; i < Endpoints.Count; i++)
            foreach (var f in Endpoints[i].Validate($"{prefix}.Endpoints[{i}]"))
                yield return f;

        for (var i = 0; i < Pools.Count; i++)
            foreach (var f in Pools[i].Validate($"{prefix}.Pools[{i}]"))
                yield return f;

        for (var i = 0; i < Regions.Count; i++)
        {
            foreach (var f in Regions[i].Validate($"{prefix}.Regions[{i}]")) yield return f;

            // Cross-ref check needs NamedAttributes — done here, not in
            // CacheRegionOptions.Validate (which doesn't see siblings).
            // Mirrors cppcache CacheParser.cpp:777-786.
            var refId = Regions[i].RefId;
            if (!string.IsNullOrEmpty(refId) && !NamedAttributes.ContainsKey(refId))
                yield return $"{prefix}.Regions[{i}].RefId='{refId}' does not match any key in {prefix}.NamedAttributes.";
        }
    }
}
