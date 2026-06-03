using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Per-entry change inside a <see cref="RegionCommit"/> — one local
/// entry operation (put / destroy / invalidate / etc.) the server tells
/// us happened during commit. Mirrors cppcache <c>FarSideEntryOp</c>
/// (<c>cppcache/src/FarSideEntryOp.hpp/.cpp</c>).
/// </summary>
internal sealed class FarSideEntryOp(MemberListForVersionStamp memberListForVersionStamp)
{
    private readonly MemberListForVersionStamp _memberListForVersionStamp = memberListForVersionStamp;
    private FarSideEntryOperation _op = FarSideEntryOperation.Marker;
    private int _modSerialNum;
    private int _eventOffset;
    private object? _key;
    private object? _value;
    private bool _didDestroy;
    private object? _callbackArg;
    private VersionTag? _versionTag;

    public void FromData(DataInput input, bool largeModCount, ushort memId)
    {
        // TODO: cppcache FarSideEntryOp::fromData (FarSideEntryOp.cpp:51-102)
        //   reads key, op byte, modSerialNum (4 or 1 byte by largeModCount),
        //   callbackArg, skipFilterRoutingInfo, versionTag via
        //   TcrMessage::readVersionTagPart, eventOffset, then op-dependent
        //   didDestroy + value (with TOKEN_INVALID/LOCAL_INVALID/DESTROYED/
        //   REMOVED/REMOVED2 = 141-145 → null short-circuit).
        throw new NotImplementedException();
    }

    public void Apply(RegionInternal region)
    {
        // TODO: cppcache FarSideEntryOp::apply (FarSideEntryOp.cpp:104-116)
        //   isDestroy(_op)    → region.TxDestroy(key, callbackArg, versionTag)
        //   isInvalidate(_op) → region.TxInvalidate(...)
        //   else              → region.TxPut(key, value, callbackArg, versionTag)
        //   RegionInternal.TxPut / TxDestroy / TxInvalidate not yet ported —
        //   add when this method ports.
        throw new NotImplementedException();
    }
}

/// <summary>
/// Wire operation kind for <see cref="FarSideEntryOp"/>. Mirrors cppcache
/// <c>FarSideEntryOp::Operation</c>
/// (<c>cppcache/src/FarSideEntryOp.hpp:38-86</c>). Backed by
/// <see langword="sbyte"/> — values are wire-significant, do NOT renumber.
/// </summary>
internal enum FarSideEntryOperation : sbyte
{
    Marker = 0,
    Create,
    PutAllCreate,
    Get,
    GetEntry,
    ContainsKey,
    ContainsValue,
    ContainsValueForKey,
    FunctionExecution,
    SearchCreate,
    LocalLoadCreate,
    NetLoadCreate,
    Update,
    PutAllUpdate,
    SearchUpdate,
    LocalLoadUpdate,
    NetLoadUpdate,
    Invalidate,
    LocalInvalidate,
    Destroy,
    LocalDestroy,
    EvictDestroy,
    RegionLoadSnapshot,
    RegionLocalDestroy,
    RegionCreate,
    RegionClose,
    RegionDestroy,
    ExpireDestroy,
    ExpireLocalDestroy,
    ExpireInvalidate,
    ExpireLocalInvalidate,
    RegionExpireDestroy,
    RegionExpireLocalDestroy,
    RegionExpireInvalidate,
    RegionExpireLocalInvalidate,
    RegionLocalInvalidate,
    RegionInvalidate,
    RegionClear,
    RegionLocalClear,
    CacheCreate,
    CacheClose,
    ForcedDisconnect,
    RegionReinitialize,
    CacheReconnect,
    PutIfAbsent,
    Replace,
    Remove,
}
