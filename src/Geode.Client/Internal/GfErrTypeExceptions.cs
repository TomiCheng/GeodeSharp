namespace Geode.Client.Internal;

/// <summary>
/// cppcache reference helper — mirrors <c>throwExceptionIfError</c> +
/// <c>GfErrTypeThrowException</c> (<c>cppcache/src/util/exception.hpp</c>
/// + <c>cppcache/src/ExceptionTypes.cpp</c>). <b>Not on the C# runtime
/// path.</b>
/// </summary>
/// <remarks>
/// <para>
/// cppcache routes errors as <see cref="GfErrType"/> codes through
/// the <c>*NoThrow</c> CRUD pipeline and only converts them to typed
/// exceptions at the top via <c>throwExceptionIfError</c> (with a
/// thread-local string stash carrying the message). The C# port
/// skips that round-trip: every <c>*NoThrow</c> method here returns
/// plain <see cref="Task"/> and any failure throws the matching
/// <see cref="GeodeException"/> subclass / BCL exception at the
/// source site, propagating via the awaited <see cref="Task"/>.
/// </para>
/// <para>
/// This helper therefore has <b>no live callers</b>. It exists so
/// (a) cppcache greps for <c>throwExceptionIfError</c> land here,
/// and (b) the <see cref="GfErrType"/> → <see cref="GeodeException"/>
/// dispatch table (cppcache's <c>get_error_map()</c>) has a
/// declared home if a translation use-case ever surfaces (e.g.
/// server-sent error codes on the wire that need typed exception
/// equivalents).
/// </para>
/// </remarks>
internal static class GfErrTypeExceptions
{
    /// <summary>
    /// Guard that turns a non-success <see cref="GfErrType"/> code into
    /// the corresponding exception. Mirrors cppcache
    /// <c>throwExceptionIfError</c>
    /// (<c>cppcache/src/util/exception.hpp:35-39</c>).
    /// </summary>
    /// <param name="str">Call-site label (e.g. <c>"Region::put"</c>);
    /// threaded into the exception message so cppcache-style
    /// "where" prefixes survive the port.</param>
    /// <param name="err">Error code returned by the
    /// <c>*NoThrow</c>-family pipeline.</param>
    public static void ThrowExceptionIfError(string str, GfErrType err)
    {
        if (err != GfErrType.NoErr)
        {
            GfErrTypeThrowException(str, err);
        }
    }

    /// <summary>
    /// Dispatch a non-success <paramref name="err"/> to the matching
    /// <see cref="GeodeException"/> subclass. Mirrors cppcache
    /// <c>GfErrTypeThrowException</c>
    /// (<c>cppcache/src/ExceptionTypes.cpp:485-501</c>) — cppcache
    /// looks up <paramref name="err"/> in a <c>get_error_map()</c>
    /// dispatch table (<c>GfErrType</c> → exception-factory callable)
    /// and invokes it; <c>[[noreturn]]</c> there, <c>[DoesNotReturn]</c>
    /// here.
    /// </summary>
    /// <remarks>
    /// Phase 1.x: NIE stub — the dispatch table lands when the
    /// <c>updateNoThrow&lt;TAction&gt;</c> pipeline grows non-trivial
    /// branches. Today every <c>*NoThrow</c> body either returns
    /// <see cref="GfErrType.NoErr"/> or the call site throws BCL
    /// exceptions directly, so this method is structurally unreachable.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void GfErrTypeThrowException(string str, GfErrType err) =>
        throw new NotImplementedException(
            $"GfErrType → exception dispatch table not yet ported. " +
            $"Call site: {str}, code: {err}.");
}
