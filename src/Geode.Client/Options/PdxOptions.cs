namespace Geode.Client.Options;

/// <summary>
/// PDX-serialisation settings mirrored from cppcache
/// <c>SystemProperties</c>. PDX is Phase 11 per CLAUDE.md, so the flag
/// below is dormant until then.
/// </summary>
public class PdxOptions : ICloneable
{
    public PdxOptions() { }

    public PdxOptions(PdxOptions other)
    {
        ClearTypeIdsOnDisconnect = other.ClearTypeIdsOnDisconnect;
    }

    /// <summary>
    /// Whether to flush the cached PDX type-id table when the client
    /// disconnects from the server. Mirrors cppcache
    /// <c>on-client-disconnect-clear-pdxType-Ids</c>; default
    /// <c>false</c>.
    /// </summary>
    public bool ClearTypeIdsOnDisconnect { get; set; }

    /// <summary>Deep clone via copy constructor.</summary>
    public PdxOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
