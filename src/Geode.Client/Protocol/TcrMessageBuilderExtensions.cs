using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

internal static class TcrMessageBuilderExtensions
{
    public static TcrMessageBuilder AddValuePart(this TcrMessageBuilder builder, object? value)
    {
        return builder.AddPart(async ct =>
        {
            using var output = ActivatorUtilities.CreateInstance<DataOutput>(builder.ServiceProvider);
            await builder.SerializationRegistry.WriteObjectAsync(output, value, ct: ct);
            return new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        });
    }
    public static TcrMessageBuilder AddCallbackArgument(this TcrMessageBuilder builder, object? callbackArgument)
    {
        if (callbackArgument == null)
        {
            return builder;
        }

        return builder.AddPart(async ct =>
        {
            using var output = ActivatorUtilities.CreateInstance<DataOutput>(builder.ServiceProvider);
            await builder.SerializationRegistry.WriteObjectAsync(output, callbackArgument, ct: ct);
            return new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        });
    }
}
