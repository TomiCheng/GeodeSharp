using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal.Entry;

internal abstract class LruAction
{
    internal enum Action
    {
        Invalidate = 0,
        LocalInvalidate,
        Destroy,
        LocalDestroy,
        InvalidAction,
        OverflowToDisk,
    }

    public virtual bool Invalidates => false;
    public virtual bool Destroys => false;
    public virtual bool Distributes => false;
    public virtual bool Overflows => false;
    public abstract Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default);
    public static LruAction NewLruAction(IServiceProvider serviceProvider,
        Action action, LocalRegion region, LruEntriesMap lRUEntriesMap)
    {
        return action switch
        {
            Action.Invalidate => ActivatorUtilities.CreateInstance<LruLocalInvalidateAction>(serviceProvider, region),
            Action.LocalDestroy => ActivatorUtilities.CreateInstance<LruLocalDestroyAction>(serviceProvider, region),
            Action.OverflowToDisk => ActivatorUtilities.CreateInstance<LruOverFlowToDiskAction>(serviceProvider, region, lRUEntriesMap),
            Action.Destroy => ActivatorUtilities.CreateInstance<LruDestroyAction>(serviceProvider, region),
            _ => throw new ArgumentException($"Unsupported LRU eviction action: {action}.", nameof(action)),
        };
    }

    public abstract Action ActionType { get; }
}
