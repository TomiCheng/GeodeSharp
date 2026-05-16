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
public class SerializationOptions : ICloneable
{
    public SerializationOptions() { }

    public SerializationOptions(SerializationOptions other)
    {
        MaxDepth = other.MaxDepth;
        MaxArrayLength = other.MaxArrayLength;
        MaxBytesLength = other.MaxBytesLength;
        MaxStringLength = other.MaxStringLength;
    }

    /// <summary>
    /// Maximum nested-container depth allowed when serialising or
    /// deserialising wire payloads. Default <b>64</b> (matches
    /// <see cref="System.Text.Json.JsonSerializerOptions.MaxDepth"/>).
    /// Must be <c>&gt;= 1</c>; validated at host build time by
    /// <c>GeodeClientOptionsValidator</c>.
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// Maximum element count for any single length-prefixed wire
    /// payload — <see cref="byte"/><c>[]</c>, primitive arrays
    /// (<see cref="int"/><c>[]</c> / <see cref="double"/><c>[]</c> /
    /// …), <c>CacheableObjectArray</c> / <c>CacheableStringArray</c>,
    /// and the Tier B-2 collection types
    /// (<c>List&lt;T&gt;</c> / <c>HashSet&lt;T&gt;</c> /
    /// <c>Dictionary&lt;K,V&gt;</c> / <c>LinkedList&lt;T&gt;</c> /
    /// <c>Stack&lt;T&gt;</c>). Default <b>1,000,000</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threat model.</b> A hostile or buggy server can claim any
    /// <c>int32</c> length in the wire-length prefix. Without a cap,
    /// the client reads "I will give you 2,000,000,000 elements" and
    /// allocates 8&#xA0;GB up front — instant OOM. Capping the
    /// length on read forces a fast, clear failure
    /// (<c>GeodeException</c>) instead.
    /// </para>
    /// <para>
    /// <b>Inclusive bound.</b> The check is <c>length &gt; MaxArrayLength</c>
    /// — a payload claiming exactly <see cref="MaxArrayLength"/>
    /// elements is accepted; one element more is rejected. <c>0</c>
    /// is the unit-of-measure (allow only empty arrays);
    /// <see cref="GeodeClientOptionsValidator"/> rejects negative
    /// values.
    /// </para>
    /// <para>
    /// <b>Default rationale.</b> Geode best practice is to keep a
    /// single cached value under ~1&#xA0;MB. A million elements
    /// covers <c>int[]</c> up to 4&#xA0;MB and <c>byte[]</c> up to
    /// 1&#xA0;MB — comfortably above realistic workloads while
    /// rejecting hostile gigabyte-scale claims. Tune up for legitimate
    /// bulk-data use cases.
    /// </para>
    /// <para>
    /// <b>Write side</b> applies the same cap (caller bug guard) —
    /// crossing the limit on encode throws
    /// <see cref="InvalidOperationException"/>; on decode it throws
    /// <c>GeodeException</c>. Symmetric so we never emit a payload
    /// our own reader would refuse.
    /// </para>
    /// </remarks>
    public int MaxArrayLength { get; set; } = 1_000_000;

    /// <summary>
    /// Maximum byte count for a single <see cref="byte"/><c>[]</c>
    /// payload (<see cref="DSCode.CacheableBytes"/> = 46). Default
    /// <b>10,000,000</b> (10&#xA0;MB).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split out from <see cref="MaxArrayLength"/> because
    /// <see cref="byte"/><c>[]</c> serves a different role on Geode:
    /// it's the canonical "binary blob" type — file content,
    /// serialised objects from another stack, encrypted payloads —
    /// which has a wholly different natural size distribution from
    /// "a primitive array with many small elements". Typical blob
    /// caches range from hundreds of KB to a few MB; the
    /// <see cref="MaxArrayLength"/> default of 1&#xA0;M would
    /// gratuitously reject those. Tune up to 100&#xA0;MB or down to
    /// 1&#xA0;MB depending on the workload.
    /// </para>
    /// <para>
    /// <b>Same threat-model and check semantics</b> as
    /// <see cref="MaxArrayLength"/>: hostile servers can claim any
    /// <c>int32</c> length in the VL-encoded prefix; on read we
    /// refuse before allocating. Inclusive bound (<c>length &gt; MaxBytesLength</c>);
    /// <c>0</c> legal; negative rejected at host build time.
    /// </para>
    /// </remarks>
    public int MaxBytesLength { get; set; } = 10_000_000;

    /// <summary>
    /// Maximum length for any single string payload — covers all four
    /// <c>CacheableString</c> wire variants (DSCodes 42 / 87 / 88 /
    /// 89). Unit is whatever the wire format puts in the length
    /// prefix for that variant (modified-UTF-8 byte count for 42,
    /// char count for the other three). Default <b>1,000,000</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threat model</b> matches <see cref="MaxArrayLength"/>: the
    /// "huge" string variants (88 / 89) carry an i32 length prefix
    /// which a hostile server can pin at 2&#xA0;billion, forcing a
    /// multi-gigabyte allocation. Cap on read prevents that.
    /// </para>
    /// <para>
    /// <b>Why a separate limit from <see cref="MaxArrayLength"/>.</b>
    /// Strings and bulk-data arrays have different "natural" size
    /// distributions — a 1&#xA0;MB JSON-shaped string is not unusual,
    /// while a 1&#xA0;MB-element collection is. Keeping the two
    /// configurable independently lets users tune one without
    /// loosening the other. Same default for now (1 M) so a stock
    /// install behaves consistently.
    /// </para>
    /// <para>
    /// <b>Inclusive bound and validation</b> follow the same rules as
    /// <see cref="MaxArrayLength"/>: <c>length &gt; MaxStringLength</c>
    /// fails; <c>0</c> is legal (empty strings only); negative
    /// rejected at host build time.
    /// </para>
    /// </remarks>
    public int MaxStringLength { get; set; } = 1_000_000;

    /// <summary>Deep clone via copy constructor.</summary>
    public SerializationOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>
    /// Validate this section. Rules migrated from
    /// <c>GeodeClientOptionsValidator</c>:
    /// <see cref="MaxDepth"/> must be &gt;= 1 (zero/negative rejects every payload
    /// including top-level scalars);
    /// <see cref="MaxArrayLength"/> / <see cref="MaxBytesLength"/> /
    /// <see cref="MaxStringLength"/> must be &gt;= 0 (zero is legal — empty only).
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        if (MaxDepth < 1)
            yield return $"{prefix}.MaxDepth must be >= 1 (got {MaxDepth}).";

        if (MaxArrayLength < 0)
            yield return $"{prefix}.MaxArrayLength must be >= 0 (got {MaxArrayLength}).";

        if (MaxBytesLength < 0)
            yield return $"{prefix}.MaxBytesLength must be >= 0 (got {MaxBytesLength}).";

        if (MaxStringLength < 0)
            yield return $"{prefix}.MaxStringLength must be >= 0 (got {MaxStringLength}).";
    }
}
