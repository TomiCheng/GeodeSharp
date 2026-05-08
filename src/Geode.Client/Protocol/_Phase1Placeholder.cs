namespace Geode.Client.Protocol;

// Phase 1 lives here:
//   - BigEndianBinaryReader / BigEndianBinaryWriter
//   - TcrPart, TcrMessage records
//   - IFrameCodec, TcrFrameCodec
//
// See CLAUDE.md "Protocol 三層架構" for the wire format spec extracted from
// cppcache/src/TcrMessage.{cpp,hpp}.
