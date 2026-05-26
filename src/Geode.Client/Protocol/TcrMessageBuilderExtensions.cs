using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

internal static class TcrMessageBuilderExtensions
{
    public static TcrMessageBuilder AddValuePart(this TcrMessageBuilder builder, ThinClientRegion region, object value)
    {
        return builder.AddPart(async ct =>
        {
            var dm = region.DistributionManager;
            using var output = ActivatorUtilities.CreateInstance<DataOutput>(builder.ServiceProvider);
            await dm.Cache.SerializationRegistry.WriteObjectAsync(output, value, dm, ct: ct);
            return new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        });
    }
}
