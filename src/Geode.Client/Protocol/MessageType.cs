/*
namespace Geode.Client.Protocol;

/// <summary>
/// TCR message-type identifier (i32 big-endian on the wire).
/// Mirrors <c>enum MsgType</c> in
/// <c>cppcache/src/TcrMessage.hpp</c> (apache/geode-native).
/// </summary>
/// <remarks>
/// Negative-valued entries (<see cref="Invalid"/>,
/// <see cref="NotPublicApiWithTimeout"/>) are sentinels used by the C++
/// client internally and never appear on the wire. We keep them so the
/// numeric-to-name mapping is exhaustive when debugging.
/// Numeric gaps (57, 95, 101, 102, 104) are preserved as-is from the
/// upstream enum.
/// </remarks>
internal enum MessageType
{
    // --- sentinels (not on the wire) ---
    NotPublicApiWithTimeout = -2,
    Invalid = -1,

    // --- core CRUD + lifecycle ---
    Request = 0,    // GET
    Response = 1,    // reply to Request
    Exception = 2,    // server-side error
    RequestDataError = 3,
    DataNotFoundError = 4,    // not in use
    Ping = 5,
    Reply = 6,    // generic ack
    Put = 7,
    PutDataError = 8,
    Destroy = 9,    // remove single key
    DestroyDataError = 10,
    DestroyRegion = 11,
    DestroyRegionDataError = 12,
    ClientNotification = 13,
    UpdateClientNotification = 14,
    LocalInvalidate = 15,
    LocalDestroy = 16,
    LocalDestroyRegion = 17,
    CloseConnection = 18,   // graceful disconnect
    ProcessBatch = 19,
    RegisterInterest = 20,
    RegisterInterestDataError = 21,
    UnregisterInterest = 22,
    UnregisterInterestDataError = 23,
    RegisterInterestList = 24,
    UnregisterInterestList = 25,
    UnknownMessageTypeError = 26,
    LocalCreate = 27,
    LocalUpdate = 28,
    CreateRegion = 29,
    CreateRegionDataError = 30,
    MakePrimary = 31,
    ResponseFromPrimary = 32,
    ResponseFromSecondary = 33,
    Query = 34,   // OQL
    QueryDataError = 35,
    ClearRegion = 36,
    ClearRegionDataError = 37,
    ContainsKey = 38,
    ContainsKeyDataError = 39,
    KeySet = 40,
    KeySetDataError = 41,

    // --- continuous queries (CQ) ---
    ExecuteCq = 42,
    ExecuteCqWithIr = 43,
    StopCq = 44,
    CloseCq = 45,
    CloseClientCqs = 46,
    CqDataError = 47,
    GetCqStats = 48,
    MonitorCq = 49,
    CqException = 50,

    // --- registration / lifecycle (continued) ---
    RegisterInstantiators = 51,
    PeriodicAck = 52,
    ClientReady = 53,
    ClientMarker = 54,
    InvalidateRegion = 55,
    PutAll = 56,   // bulk PUT
    // 57 — not assigned upstream
    GetAllDataError = 58,

    // --- function execution ---
    ExecuteRegionFunction = 59,
    ExecuteRegionFunctionResult = 60,
    ExecuteRegionFunctionError = 61,
    ExecuteFunction = 62,
    ExecuteFunctionResult = 63,
    ExecuteFunctionError = 64,

    // --- client interest / metadata ---
    ClientRegisterInterest = 65,
    ClientUnregisterInterest = 66,
    RegisterDataSerializers = 67,
    RequestEventValue = 68,
    RequestEventValueError = 69,
    PutDeltaError = 70,
    GetClientPrMetadata = 71,
    ResponseClientPrMetadata = 72,
    GetClientPartitionAttributes = 73,
    ResponseClientPartitionAttributes = 74,
    GetClientPrMetadataError = 75,
    GetClientPartitionAttributesError = 76,

    // --- auth ---
    UserCredentialMessage = 77,
    RemoveUserAuth = 78,

    ExecuteRegionFunctionSingleHop = 79,
    QueryWithParameters = 80,
    Size = 81,
    SizeError = 82,
    Invalidate = 83,
    InvalidateError = 84,

    // --- transactions ---
    Commit = 85,
    CommitError = 86,
    Rollback = 87,
    TxFailover = 88,
    GetEntry = 89,
    TxSynchronization = 90,
    GetFunctionAttributes = 91,

    // --- PDX ---
    GetPdxTypeById = 92,
    GetPdxIdForType = 93,
    AddPdxType = 94,
    // 95 — not assigned upstream
    AddPdxEnum = 96,
    GetPdxIdForEnum = 97,
    GetPdxEnumById = 98,

    ServerToClientPing = 99,   // server-initiated keepalive
    GetAll70 = 100,  // bulk GET (Geode 7.0+ wire)
    // 101, 102, 104 — not assigned upstream
    TombstoneOperation = 103,
    GetDurableCqs = 105,
    GetDurableCqsDataError = 106,
    GetAllWithCallback = 107,
    PutAllWithCallback = 108,
    RemoveAll = 109,
}

*/