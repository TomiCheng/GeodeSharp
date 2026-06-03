using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

internal static class TcrMessageExtensions
{
    /// <summary>
    /// Deserialise the message's first body part into <typeparamref name="T"/>.
    /// Mirrors cppcache <c>TcrMessage::getValue</c>
    /// (<c>cppcache/src/TcrMessage.cpp:3049</c>) — returns the cached
    /// <c>m_value</c>. Our port reconstructs it on demand from
    /// <c>Parts[0]</c> via <see cref="SerializationRegistry.ReadObject"/>.
    /// </summary>
    public static T? GetValue<T>(this TcrMessage message) where T : class
    {
        if (message.Parts.Count == 0) return null;
        var reader = new DataInput(message.Parts[0].Payload);
        var registry = message.ServiceProvider.GetRequiredService<SerializationRegistry>();
        return registry.ReadObject(reader) as T;
    }
    public static string GetException(this TcrMessage message)
    {
        // cppcache TcrMessage::getException (TcrMessage.cpp:213-216) does
        //   m_exceptionMessage = Utils::nullSafeToString(m_value); return ref;
        // where m_value is the deserialised exception payload from an EXCEPTION
        // reply. Until the Java exception object → C# deserialiser lands we
        // route through the existing ASCII-preview helper which already does
        // the best-effort stringification.
        return TcrMessageHelper.DecodeExceptionPreview(message);
    }
}
