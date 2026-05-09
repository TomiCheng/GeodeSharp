using System.Buffers;
using System.Text;

namespace Geode.Client.Protocol.Operations;

/// <summary>
/// <see cref="MessageType.Put"/> operation. Mirrors cppcache
/// <c>ThinClientRegion::putNoThrow_remote</c>
/// (<c>cppcache/src/ThinClientRegion.cpp:888</c>).
/// </summary>
internal static class PutExtensions
{
    // DSCode literals — to migrate to a shared Protocol/DSCode.cs once
    // a few more land.
    private const byte DSCodeNullObj = 41;          // 0x29
    private const byte DSCodeCacheableBoolean = 53; // 0x35

    // EventId per-i64 type code. cppcache EventId::writeIdsData
    // (cppcache/src/EventId.hpp line 95) always emits 3 = "long".
    private const byte EventIdLongCode = 3;

    /// <summary>
    /// Build a <see cref="MessageType.Put"/> request frame. Pure function;
    /// no I/O.
    /// </summary>
    /// <remarks>
    /// Phase 3 only handles <c>string</c> keys and <c>byte[]</c> values
    /// (the walking-skeleton subset). Phase 4 expands to int / long /
    /// bool / Date via a serialization registry; this signature is stable.
    /// </remarks>
    public static TcrMessage BuildPut(
        string regionName,
        object key,
        object? value,
        object? callbackArgument,
        long eventThreadId,
        long eventSequenceId,
        int transactionId,
        bool isDelta = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);

        // Part 1 — Region name. Raw ASCII bytes, IsObject=false.
        var regionPayload = Encoding.ASCII.GetBytes(regionName);
        var regionPart = new TcrPart(IsObject: 0, Payload: regionPayload);

        // Part 2 — Operation = NullObj. DSCode 41 byte.
        var nullObjPart = new TcrPart(
            IsObject: 1,
            Payload: new byte[] { DSCodeNullObj });

        // Part 3 — Flags i32 = 0. Raw 4 bytes BE.
        var flagsPart = new TcrPart(
            IsObject: 0,
            Payload: new byte[4]);

        // Part 4 — Key. Phase 3: string only, encode via WriteString.
        if (key is not string keyString)
        {
            throw new NotSupportedException(
                $"Phase 3 only supports string keys; got {key.GetType()}.");
        }
        var keyBuffer = new ArrayBufferWriter<byte>();
        var keyWriter = new BigEndianBinaryWriter(keyBuffer);
        keyWriter.WriteString(keyString);
        var keyPart = new TcrPart(
            IsObject: 1,
            Payload: keyBuffer.WrittenMemory);

        // Part 5 — isDelta as CacheableBoolean. DSCode 53 + 1 byte.
        var isDeltaPart = new TcrPart(
            IsObject: 1,
            Payload: new byte[] { DSCodeCacheableBoolean, isDelta ? (byte)1 : (byte)0 });

        // Part 6 — Value. Phase 3: byte[] only.
        // CacheableBytes special case (cppcache writeObjectPart, line 676):
        // raw bytes with IsObject=0, no DSCode 46 wrapper, no varint length
        // prefix. Empty byte[] would need IsObject=2 — we reject it.
        if (value is null)
        {
            throw new NotSupportedException(
                "Phase 3 does not support null value (Geode treats it as " +
                "invalidate, not put). Use a future Destroy / Invalidate op.");
        }
        if (value is not byte[] valueBytes)
        {
            throw new NotSupportedException(
                $"Phase 3 only supports byte[] values; got {value.GetType()}.");
        }
        if (valueBytes.Length == 0)
        {
            throw new NotSupportedException(
                "Phase 3 does not support empty byte[] values (the wire " +
                "encoding requires IsObject=2 which would widen TcrPart).");
        }
        var valuePart = new TcrPart(IsObject: 0, Payload: valueBytes);

        // Part 7 — EventId. 18 raw bytes:
        //   [u8 longCode=3][i64 threadId BE][u8 longCode=3][i64 sequenceId BE]
        var eventIdBuffer = new ArrayBufferWriter<byte>(18);
        var eventIdWriter = new BigEndianBinaryWriter(eventIdBuffer);
        eventIdWriter.WriteByte(EventIdLongCode);
        eventIdWriter.WriteInt64(eventThreadId);
        eventIdWriter.WriteByte(EventIdLongCode);
        eventIdWriter.WriteInt64(eventSequenceId);
        var eventIdPart = new TcrPart(
            IsObject: 0,
            Payload: eventIdBuffer.WrittenMemory);

        // Part 8 — Callback argument. Optional. Phase 3 only handles null
        // (skip the part) or string callback.
        var parts = new List<TcrPart>(8)
        {
            regionPart,
            nullObjPart,
            flagsPart,
            keyPart,
            isDeltaPart,
            valuePart,
            eventIdPart,
        };
        if (callbackArgument is not null)
        {
            if (callbackArgument is not string cbString)
            {
                throw new NotSupportedException(
                    $"Phase 3 only supports null or string callback argument; " +
                    $"got {callbackArgument.GetType()}.");
            }
            var cbBuffer = new ArrayBufferWriter<byte>();
            var cbWriter = new BigEndianBinaryWriter(cbBuffer);
            cbWriter.WriteString(cbString);
            parts.Add(new TcrPart(IsObject: 1, Payload: cbBuffer.WrittenMemory));
        }

        return new TcrMessage(
            MessageType: MessageType.Put,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
