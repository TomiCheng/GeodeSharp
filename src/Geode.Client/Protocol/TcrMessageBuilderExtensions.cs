using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

internal static class TcrMessageBuilderExtensions
{
    public static TcrMessageBuilder AddValuePart(this TcrMessageBuilder builder, GeodeCache cache, object value)
    {
        return builder.AddPart(async ct =>
        {
            using var output = ActivatorUtilities.CreateInstance<DataOutput>(builder.ServiceProvider);
            await cache.SerializationRegistry.WriteObjectAsync(output, value, ct: ct);
            return new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        });
    }
}
