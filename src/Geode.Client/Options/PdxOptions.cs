namespace Geode.Client.Options;

/// <summary>
/// PDX-serialisation settings mirrored from cppcache
/// <c>SystemProperties</c>. PDX is Phase 11 per CLAUDE.md, so the flag
/// below is dormant until then.
/// </summary>
public class PdxOptions
{
    /// <summary>
    /// Whether to flush the cached PDX type-id table when the client
    /// disconnects from the server. Mirrors cppcache
    /// <c>on-client-disconnect-clear-pdxType-Ids</c>; default
    /// <c>false</c>.
    /// </summary>
    public bool ClearTypeIdsOnDisconnect { get; set; }

    /// <summary>Deep clone. Single bool — MemberwiseClone is sufficient.</summary>
    public PdxOptions DeepClone() => (PdxOptions)MemberwiseClone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
