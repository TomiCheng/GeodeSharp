/*
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

    object ICloneable.Clone() => Clone();

    /// <summary>Deep clone via copy constructor.</summary>
    public PdxOptions Clone() => new(this);

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }

    /// <summary>
    /// Whether to flush the cached PDX type-id table when the client disconnects from the server.
    /// </summary>
    public bool ClearTypeIdsOnDisconnect { get; set; }

}

*/