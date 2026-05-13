namespace Geode.Client.Options;

/// <summary>
/// Transaction-related settings mirrored from cppcache
/// <c>SystemProperties</c>. Out of MVP scope per CLAUDE.md, included
/// only for parity during the audit window.
/// </summary>
public class TxOptions
{
    /// <summary>
    /// How long the server retains a suspended transaction's state
    /// before discarding it. Mirrors cppcache <c>suspended-tx-timeout</c>;
    /// default 30 seconds.
    /// </summary>
    public TimeSpan SuspendedTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Deep clone. TimeSpan-only — MemberwiseClone is sufficient.</summary>
    public TxOptions DeepClone() => (TxOptions)MemberwiseClone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
