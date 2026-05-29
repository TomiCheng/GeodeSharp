using System.Collections.Generic;

namespace Geode.Client.Internal;

/// <summary>
/// Mutex-guarded MRU-ordered queue of <see cref="MapEntry"/> backing LRU
/// eviction: newest at the tail, least-recently-used at the head. Mirrors
/// cppcache <c>LRUQueue</c> (<c>cppcache/src/LRUQueue.hpp:39</c>).
/// </summary>
/// <remarks>
/// cppcache is <i>intrusive</i> — the list iterator lives on the entry
/// (<c>LRUEntryProperties.iter_</c>) so <see cref="Remove"/> /
/// <see cref="MoveToEnd"/> are O(1) without a side table. This port keeps
/// the queue self-contained: a <see cref="LinkedList{T}"/> plus an
/// entry→node <see cref="Dictionary{TKey, TValue}"/> for the same O(1)
/// lookups, decoupled from <see cref="MapEntry"/>. If memory pressure
/// later argues for the intrusive form, the node moves onto
/// <see cref="LRUEntryProperties.Node"/> and this dictionary drops out.
/// All operations lock <see cref="_lock"/> (cppcache <c>mutex_</c>).
/// </remarks>
internal sealed class LRUQueue
{
    private readonly object _lock = new();
    private readonly LinkedList<MapEntry> _container = new();
    private readonly Dictionary<MapEntry, LinkedListNode<MapEntry>> _nodes = [];

    /// <summary>Number of entries in the queue. cppcache <c>size()</c>.</summary>
    public int Count
    {
        get { lock (_lock) { return _container.Count; } }
    }

    /// <summary>
    /// Push <paramref name="entry"/> at the tail (MRU). No-op if already
    /// queued. cppcache <c>push</c> (<c>LRUQueue.hpp:53</c>).
    /// </summary>
    public void Push(MapEntry entry)
    {
        lock (_lock)
        {
            if (!_nodes.ContainsKey(entry))
            {
                _nodes[entry] = _container.AddLast(entry);
            }
        }
    }

    /// <summary>
    /// Pop the head entry (LRU), or <see langword="null"/> if empty.
    /// cppcache <c>pop</c> (<c>LRUQueue.hpp:60</c>).
    /// </summary>
    public MapEntry? Pop()
    {
        lock (_lock)
        {
            var head = _container.First;
            if (head is null)
            {
                return null;
            }
            _container.RemoveFirst();
            _nodes.Remove(head.Value);
            return head.Value;
        }
    }

    /// <summary>Remove <paramref name="entry"/> from the queue. cppcache <c>remove</c> (<c>LRUQueue.hpp:66</c>).</summary>
    public void Remove(MapEntry entry)
    {
        lock (_lock)
        {
            if (_nodes.Remove(entry, out var node))
            {
                _container.Remove(node);
            }
        }
    }

    /// <summary>
    /// Move <paramref name="entry"/> to the tail (MRU) on access. No-op if
    /// not queued. cppcache <c>move_to_end</c> (<c>LRUQueue.hpp:72</c>).
    /// </summary>
    public void MoveToEnd(MapEntry entry)
    {
        lock (_lock)
        {
            if (_nodes.TryGetValue(entry, out var node))
            {
                _container.Remove(node);   // detaches node; safe to re-add
                _container.AddLast(node);
            }
        }
    }

    /// <summary>Empty the queue. cppcache <c>clear()</c> (<c>LRUQueue.hpp:77</c>).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _container.Clear();
            _nodes.Clear();
        }
    }
}
