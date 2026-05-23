/*
namespace Geode.Client.Options;

/// <summary>
/// TLS / SSL configuration. Mirrors cppcache <c>ssl-*</c> settings but
/// will eventually layer on top of <c>System.Net.Security.SslStream</c>
/// (Phase 8) — file paths may be replaced or augmented with
/// <c>X509Certificate2</c> handles when we get there.
/// </summary>
public class TlsOptions : ICloneable
{
    public TlsOptions() { }

    public TlsOptions(TlsOptions other)
    {
        Enabled = other.Enabled;
        KeyStorePath = other.KeyStorePath;
        KeyStorePassword = other.KeyStorePassword;
        TrustStorePath = other.TrustStorePath;
    }

    /// <summary>
    /// Whether to upgrade the socket with TLS after TCP connect. Mirrors
    /// cppcache <c>ssl-enabled</c>; default <c>false</c>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Path to the client keystore (.pem in cppcache). Mirrors cppcache
    /// <c>ssl-keystore</c>; default empty.
    /// </summary>
    public string KeyStorePath { get; set; } = string.Empty;

    /// <summary>
    /// Password protecting the keystore at <see cref="KeyStorePath"/>.
    /// Mirrors cppcache <c>ssl-keystore-password</c>; default empty.
    /// </summary>
    public string KeyStorePassword { get; set; } = string.Empty;

    /// <summary>
    /// Path to the truststore used to validate the server certificate
    /// chain. Mirrors cppcache <c>ssl-truststore</c>; default empty.
    /// </summary>
    public string TrustStorePath { get; set; } = string.Empty;

    /// <summary>Deep clone via copy constructor.</summary>
    public TlsOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}

*/