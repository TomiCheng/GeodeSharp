namespace Geode.Client.Options;

/// <summary>
/// Wire-serialisation robustness / safety settings. No cppcache
/// analogue — cppcache trusts the wire stream and lets any nesting
/// depth through. We add a configurable bound so a hostile or buggy
/// server cannot crash the client with a stack-overflow DoS via
/// arbitrarily-nested <c>CacheableArrayList</c> / <c>CacheableHashMap</c>
/// payloads.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="System.Text.Json.JsonSerializerOptions.MaxDepth"/>
/// (same default 64, same threat model — untrusted JSON / wire data
/// recursing through nested objects until the runtime stack runs out).
/// Newtonsoft.Json shipped without a limit historically and earned a
/// CVE for it; we'd rather start tight and loosen if real workloads
/// complain than the other way round.
/// </para>
/// <para>
/// <b>Scope.</b> The limit governs recursive descent inside
/// <c>SerializationRegistry.WriteObject</c> /
/// <c>SerializationRegistry.ReadObject</c> — every nested
/// <c>CacheableArrayList</c> / <c>CacheableHashSet</c> /
/// <c>CacheableHashMap</c> / <c>CacheableLinkedList</c> /
/// <c>CacheableStack</c> / <c>CacheableObjectArray</c> /
/// <c>CacheableStringArray</c> layer counts as one level. Scalars and
/// primitive arrays do not contribute (they don't recurse). Realistic
/// payloads almost never exceed 5–10 levels.
/// </para>
/// <para>
/// <b>Behaviour at the limit.</b> Read side throws
/// <c>GeodeException</c> (wire-level error). Write side throws
/// <c>InvalidOperationException</c> (caller bug — probably a cycle in
/// the in-memory graph). Symmetric so a round-trip never produces a
/// payload our own reader would refuse.
/// </para>
/// </remarks>
public class SerializationOptions
{
    /// <summary>
    /// Maximum nested-container depth allowed when serialising or
    /// deserialising wire payloads. Default <b>64</b> (matches
    /// <see cref="System.Text.Json.JsonSerializerOptions.MaxDepth"/>).
    /// Must be <c>&gt;= 1</c>; validated at host build time by
    /// <c>GeodeClientOptionsValidator</c>.
    /// </summary>
    public int MaxDepth { get; set; } = 64;
}
