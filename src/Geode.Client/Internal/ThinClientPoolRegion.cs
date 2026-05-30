using System.Runtime.Intrinsics.Arm;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Pool-mode region subclass. Mirrors cppcache
/// <c>ThinClientPoolRegion</c>
/// (<c>cppcache/src/ThinClientPoolRegion.hpp:28</c>) — the variant of
/// <see cref="ThinClientRegion"/> used when the cache is configured
/// for pool-mode access (which is our default and only mode).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.5 skeleton:</b> empty body. Exists so the cppcache
/// inheritance shape <c>ThinClientRegion</c> &#x2192;
/// <c>ThinClientPoolRegion</c> is present from the start; concrete
/// migrations land step by step:
/// </para>
/// <list type="bullet">
/// <item><description><c>initTCR()</c> override
/// (<c>ThinClientPoolRegion.hpp:39</c>) — pool-mode TCR initialisation
/// (resolving the pool by name, attaching the DM, kicking off
/// subscription redundancy if needed).</description></item>
/// <item><description><c>destroyDM(bool keepEndpoints)</c> override
/// (<c>ThinClientPoolRegion.hpp:43</c>) — pool-mode DM teardown that
/// leaves shared pool endpoints alive when the region is destroyed but
/// the pool keeps serving other regions.</description></item>
/// </list>
/// <para>
/// Until <see cref="RegionFactory"/> switches to constructing
/// <see cref="ThinClientPoolRegion"/>, pool-mode regions stay base
/// <see cref="ThinClientRegion"/> instances and this subclass is
/// unused. Pair with the unsealed base — same structural placeholder
/// pattern as <see cref="TcrPoolEndPoint"/>.
/// </para>
/// </remarks>
internal sealed class ThinClientPoolRegion(
    IServiceProvider serviceProvider,
    string name,
    RegionInternal? parent,
    RegionAttributes attributes)
    : ThinClientRegion(serviceProvider, name, parent, attributes)
{
    readonly static ObjectFactory<ThinClientPoolRegion> _objectFactory =
        ActivatorUtilities.CreateFactory<ThinClientPoolRegion>([typeof(string), typeof(RegionInternal), typeof(RegionAttributes)]);

    public new static ThinClientPoolRegion Create(IServiceProvider serviceProvider, string name, RegionInternal? parent, RegionAttributes attributes)
        => _objectFactory(serviceProvider, [ name, parent, attributes ]);
    // No fields beyond the base — cppcache ThinClientPoolRegion has no
    // private state of its own either. Override surface above.

    readonly ILogger<ThinClientPoolRegion> _logger = serviceProvider.GetRequiredService<ILogger<ThinClientPoolRegion>>();
    readonly PoolManager _poolManager = serviceProvider.GetRequiredService<PoolManager>();

    public override Task InitTcrAsync(CancellationToken ct = default)
    {
        try
        {
            var poolDM = _poolManager.Find(Attributes.PoolName) as ThinClientPoolDM;
            _dm = poolDM ?? throw new InvalidOperationException(
                    $"pool not found: '{Attributes.PoolName}' (region '{FullPath}').");
            poolDM.IncRegionCount();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize region {RegionName}", Name);
            throw;
        }

        return Task.CompletedTask;
    }
}
