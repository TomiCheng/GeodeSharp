namespace Geode.Client;

/// <summary>
/// User-implemented hook that maps an entry event to a routing object,
/// letting a client single-hop directly to the server holding a
/// partitioned region's bucket.
/// </summary>
public interface IPartitionResolver
{
    /// <summary>Identifier for this resolver.</summary>
    string Name => string.Empty;

    /// <summary>The routing object for <paramref name="opDetails"/>'s entry — entries sharing a routing object co-locate on the same bucket.</summary>
    object GetRoutingObject(EntryEvent opDetails);
}
