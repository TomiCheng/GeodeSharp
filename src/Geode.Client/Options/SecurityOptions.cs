namespace Geode.Client.Options;

/// <summary>
/// Security-related settings mirrored from cppcache
/// <c>SystemProperties</c>. Included for parity during the cppcache
/// audit window.
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md uses a flat <c>"Auth": { "Username", "Password" }</c>
/// section for credentials; the Diffie-Hellman fields below come from
/// cppcache and are documented as deprecated upstream. They are very
/// likely to be deleted before Phase 9 (auth) lands.
/// </para>
/// </remarks>
public class SecurityOptions
{
    /// <summary>
    /// Diffie-Hellman algorithm used to encrypt credentials in the
    /// handshake. Mirrors cppcache <c>security-client-dhalgo</c>;
    /// default empty. Deprecated upstream.
    /// </summary>
    public string ClientDhAlgo { get; set; } = string.Empty;

    /// <summary>
    /// Path to the client keystore used by the DH credential exchange.
    /// Mirrors cppcache <c>security-client-kspath</c>; default empty.
    /// Deprecated upstream.
    /// </summary>
    public string ClientKsPath { get; set; } = string.Empty;

    /// <summary>
    /// Free-form key/value bag forwarded to the server-side auth callback.
    /// Mirrors cppcache's <c>security-*</c> property prefix bucket
    /// (<c>m_securityPropertiesPtr</c>).
    /// </summary>
    /// <remarks>
    /// Settable (rather than init-only) so <see cref="DeepClone"/> can
    /// reassign with a new dict instance — <see cref="object.MemberwiseClone"/>
    /// copies the reference only, leaving the clone aliased to the
    /// original until we replace it.
    /// </remarks>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>Deep clone. Strings + Dictionary&lt;string,string&gt; — shallow MemberwiseClone then dict copy.</summary>
    public SecurityOptions DeepClone()
    {
        var clone = (SecurityOptions)MemberwiseClone();
        clone.Properties = new Dictionary<string, string>(Properties);
        return clone;
    }

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
