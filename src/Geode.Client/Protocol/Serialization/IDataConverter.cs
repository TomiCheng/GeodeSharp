/*
namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Codec for one built-in DSCode type pair (e.g.
/// <see cref="DSCode.CacheableInt32"/> ??<see cref="int"/>). Mirrors
/// cppcache <c>Serializable</c> family
/// (<c>cppcache/include/geode/Serializable.hpp</c>) but expressed as
/// an external codec object rather than a method on the value itself
/// ??primitives (<c>int</c>, <c>string</c>) can't be modified to
/// implement an interface, so a sidecar codec keeps the design
/// uniform.
/// </summary>
/// <remarks>
/// <para>
/// <b>Always internal.</b> Built-in DSCode types are a closed set;
/// users that need custom types go through PDX
/// (<see cref="IPdxConverter"/>, Phase 2+) which is the public
/// extension surface. Adding new built-in DSCodes is a maintainer
/// activity, not a user activity.
/// </para>
/// <para>
/// <b>One converter, possibly many DSCodes.</b> Most converters
/// handle exactly one wire DSCode (<c>int</c> ??/// <see cref="DSCode.CacheableInt32"/>). <c>string</c> is special:
/// one converter handles four DSCodes (<c>CacheableASCIIString</c> /
/// <c>?�ASCIIStringHuge</c> / <c>CacheableString</c> /
/// <c>?�StringHuge</c>) and picks which one at <see cref="Write"/>
/// time based on content. The <see cref="DsCodes"/> array is the
/// decode-side index; <see cref="GetDsCode"/> resolves the
/// encode-side choice.
/// </para>
/// <para>
/// <b>DSCode byte ownership.</b> The registry writes / reads the
/// DSCode byte on both sides of the wire; converters only handle
/// payload. The byte is passed back to the converter
/// (<see cref="Write"/> / <see cref="Read"/>) so multi-DSCode
/// converters can branch without re-scanning. Mirrors cppcache
/// <c>DataOutput::writeObject</c> which calls
/// <c>ptr-&gt;getDsCode()</c> then writes the byte then calls
/// <c>ptr-&gt;toData(*this)</c>.
/// </para>
/// <para>
/// The two-layer split below
/// (<see cref="IDataConverter"/> + <see cref="IDataConverter{T}"/>
/// + <see cref="DataConverter{T}"/>):
/// </para>
/// <list type="bullet">
///   <item><b>Non-generic <see cref="IDataConverter"/></b> ??what
///         <c>SerializationRegistry</c> stores. Heterogeneous storage
///         (<c>Dictionary&lt;byte, IDataConverter&gt;</c>) needs an
///         erased base; that's this one.</item>
///   <item><b>Generic <see cref="IDataConverter{T}"/></b> ??what
///         implementers write against; compile-time type safety on
///         <see cref="IDataConverter{T}.Write"/> / <see cref="IDataConverter{T}.Read"/>.</item>
///   <item><b>Abstract <see cref="DataConverter{T}"/></b> ??bridges
///         the two so concrete codecs only override the typed
///         methods, never the <see cref="object"/> overloads.</item>
/// </list>
/// </remarks>
internal interface IDataConverter
{
    /// <summary>
    /// All wire DSCode tags this converter handles. Used as the
    /// decode-side registry index; the registry registers one entry
    /// per element pointing at the same converter instance. Single
    /// element for most converters; four for <c>string</c>. Mirrors
    /// the implicit one-DSCode-per-class layout cppcache enforces via
    /// <c>Serializable::getDsCode()</c> ??we generalise to many
    /// because .NET represents <c>string</c> as a single CLR type.
    /// </summary>
    byte[] DsCodes { get; }

    /// <summary>
    /// CLR type this converter handles. Used as the registry encode
    /// key (runtime type ??codec lookup). Cppcache's runtime type
    /// system is implicit through <c>typeid</c>; we make it explicit
    /// because .NET dictionary keys need it.
    /// </summary>
    Type ManagedType { get; }

    /// <summary>
    /// Pick which DSCode to emit for <paramref name="value"/>. Most
    /// converters return their sole <see cref="DsCodes"/> entry;
    /// <c>string</c>'s converter inspects the content and picks one
    /// of four. Mirrors cppcache <c>Serializable::getDsCode()</c>
    /// (which is parameterless because each cppcache instance carries
    /// its DSCode; we make it stateless by passing the value in).
    /// </summary>
    byte GetDsCode(object value);

    /// <summary>
    /// Write <paramref name="value"/>'s payload to <paramref name="writer"/>.
    /// The DSCode byte is NOT written here — the registry writes it
    /// before delegating in, then passes the byte back as
    /// <paramref name="dsCode"/> so multi-DSCode converters can branch.
    /// </summary>
    /// <param name="depth">
    /// Current nesting level — <c>0</c> at the top-level call.
    /// Container converters MUST forward <c>depth + 1</c> when re-entering
    /// <see cref="SerializationRegistry.WriteObjectAsync"/>.
    /// </param>
    ValueTask WriteAsync(DataOutput writer, object value, byte dsCode, int depth, CancellationToken ct);

    /// <summary>
    /// Read one payload from <paramref name="reader"/>. The DSCode
    /// byte has already been consumed by the registry (used for codec
    /// lookup) and is passed back as <paramref name="dsCode"/> so
    /// multi-DSCode converters know which format the payload is in.
    /// Single-DSCode converters ignore it.
    /// </summary>
    /// <param name="depth">
    /// Current nesting level ??see
    /// <see cref="Write(DataOutput, object, byte, int)"/>
    /// for semantics. Container converters forward <c>depth + 1</c>
    /// when re-entering <see cref="SerializationRegistry.ReadObject"/>
    /// for each element.
    /// </param>
    /// <returns>
    /// Boxed instance of <see cref="ManagedType"/>, or <c>null</c>
    /// for value types whose stored representation is "no value".
    /// </returns>
    object? Read(BigEndianBinaryReader reader, byte dsCode, int depth);

    /// <summary>Async 版本的 <see cref="Read"/>;default interface method,wrap sync。</summary>
    ValueTask<object?> ReadAsync(BigEndianBinaryReader reader, byte dsCode, int depth, CancellationToken ct) =>
        ValueTask.FromResult(Read(reader, dsCode, depth));
}

*/