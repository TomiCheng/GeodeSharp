namespace Geode.Client.Protocol;

/// <summary>
/// Geode <b>DataSerializable type code</b> (single u8 on the wire).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>cppcache/include/geode/internal/DSCode.hpp</c>'s
/// <c>enum class DSCode</c>. Each constant is the leading byte that
/// tags the next bytes as a particular built-in type, e.g. a Part
/// payload starting <c>0x57 0x00 0x05 'h' 'e' 'l' 'l' 'o'</c> is a
/// <see cref="CacheableASCIIString"/> (<c>87</c>) of length 5.
/// </para>
/// <para>
/// On the wire <see cref="TcrPart.IsObject"/>=1 means "payload begins
/// with one of these bytes". <see cref="TcrPart.IsObject"/>=0 / 2 paths
/// skip the DSCode entirely (region name, raw <c>byte[]</c>, EventId,
/// flags, ...) — those are NOT DSCode-tagged.
/// </para>
/// <para>
/// We expose the values as <c>const byte</c> rather than a typed enum
/// because they almost always appear inline in a payload byte sequence:
/// <c>WriteByte(DSCode.NullObj)</c> reads more cleanly than
/// <c>WriteByte((byte)DSCode.NullObj)</c>, and a switch on a byte read
/// from the wire works the same with constants as with an enum.
/// </para>
/// <para>
/// Phase 3 only uses <see cref="NullObj"/>, <see cref="CacheableBoolean"/>,
/// <see cref="CacheableBytes"/>, <see cref="CacheableNullString"/>,
/// <see cref="CacheableString"/>, and <see cref="CacheableASCIIString"/>.
/// The rest are filled in here as a one-time copy from cppcache so
/// later phases just reference them.
/// </para>
/// </remarks>
internal static class DSCode
{
    // Fixed-ID prefixes — used when serialising DataSerializableFixedId
    // objects (e.g. EventId, ClientProxyMembershipId).
    public const byte FixedIDDefault = 0;
    public const byte FixedIDByte = 1;
    public const byte FixedIDShort = 2;
    public const byte FixedIDInt = 3;
    public const byte FixedIDNone = 4;

    // User-data class IDs (DataSerializable). Phase 11+ for custom types.
    public const byte CacheableUserData4 = 37;
    public const byte CacheableUserData2 = 38;
    public const byte CacheableUserData = 39;

    // Null sentinel — single-byte payload meaning "no value".
    public const byte NullObj = 41;

    // Strings.
    public const byte CacheableString = 42;          // u16 byte-len + modified UTF-8
    public const byte CacheableNullString = 69;      // bare DSCode (no body)
    public const byte CacheableASCIIString = 87;     // u16 char-len + ASCII bytes
    public const byte CacheableASCIIStringHuge = 88; // i32 char-len + ASCII bytes
    public const byte CacheableStringHuge = 89;      // i32 char-len + UTF-16 BE

    // Class / Java serializable wrappers.
    public const byte Class = 43;
    public const byte JavaSerializable = 44;
    public const byte DataSerializable = 45;

    // Byte arrays (special: usually IsObject=0 raw, not DSCode-tagged —
    // see cppcache writeObjectPart line 676).
    public const byte CacheableBytes = 46;

    // Numeric / boolean primitives (mostly Phase 4 territory).
    public const byte CacheableBoolean = 53;
    public const byte CacheableCharacter = 54;
    public const byte CacheableByte = 55;
    public const byte CacheableInt16 = 56;
    public const byte CacheableInt32 = 57;
    public const byte CacheableInt64 = 58;
    public const byte CacheableFloat = 59;
    public const byte CacheableDouble = 60;
    public const byte CacheableDate = 61;

    // Primitive arrays.
    public const byte BooleanArray = 26;
    public const byte CharArray = 27;
    public const byte CacheableInt16Array = 47;
    public const byte CacheableInt32Array = 48;
    public const byte CacheableInt64Array = 49;
    public const byte CacheableFloatArray = 50;
    public const byte CacheableDoubleArray = 51;
    public const byte CacheableObjectArray = 52;

    // Collections (Phase 5+).
    public const byte CacheableLinkedList = 10;
    public const byte CacheableFileName = 63;
    public const byte CacheableStringArray = 64;
    public const byte CacheableArrayList = 65;
    public const byte CacheableHashSet = 66;
    public const byte CacheableHashMap = 67;
    public const byte CacheableTimeUnit = 68;
    public const byte CacheableHashTable = 70;
    public const byte CacheableVector = 71;
    public const byte CacheableIdentityHashMap = 72;
    public const byte CacheableLinkedHashSet = 73;
    public const byte CacheableStack = 74;

    // Misc infra.
    public const byte Properties = 11;
    public const byte PdxType = 17;

    // PDX (Phase 11).
    public const byte PDX = 93;
    public const byte PdxEnum = 94;
}
