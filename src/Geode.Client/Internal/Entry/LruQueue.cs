using System;
using System.Collections.Generic;
using System.Text;

namespace Geode.Client.Internal.Entry;

internal class LruQueue
{
    private readonly object _lock = new();
    private readonly LinkedList<MapEntry> _container = new();
    private readonly Dictionary<MapEntry, LinkedListNode<MapEntry>> _nodes = [];

    public int Count
    {
        get { lock (_lock) { return _container.Count; } }
    }

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

    public void MoveToEnd(MapEntry entry)
    {
        lock (_lock)
        {
            if (_nodes.TryGetValue(entry, out var node))
            {
                _container.Remove(node); 
                _container.AddLast(node);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _container.Clear();
            _nodes.Clear();
        }
    }
}
