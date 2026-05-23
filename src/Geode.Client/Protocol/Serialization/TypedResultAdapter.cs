using System.Collections;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Top-edge adapter that reshapes a canonical-form value produced by
/// <see cref="SerializationRegistry.ReadObject"/> into the strongly-typed
/// shape the caller declared on <see cref="IRegion{TKey,TValue}"/>. Used
/// by <see cref="Regions.RegionView{TKey,TValue}"/> at the boundary
/// between the object-typed internal pipeline and the typed public
/// surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> Registered as DI Scoped alongside
/// <see cref="SerializationRegistry"/> so the two collaborators share a
/// cache scope. Stateless today, but Scoped leaves room for per-cache
/// reflection caches (compiled element-adders, type-walker delegates)
/// and per-cache PDX type rules without revisiting the lifetime later.
/// </para>
/// <para>
/// <b>Why this exists.</b> Java's wire format for ArrayList / HashMap /
/// HashSet does not encode the container element type — each slot is
/// tagged by per-element DSCode only — so
/// <see cref="SerializationRegistry.ReadObject"/> always returns the
/// erased canonical form (<c>List&lt;object?&gt;</c> for
/// <see cref="DSCode.CacheableArrayList"/>). The caller may declare the
/// region as <c>IRegion&lt;int, IList&lt;int&gt;&gt;</c>; this adapter
/// reshapes <c>List&lt;object?&gt;</c> into <c>List&lt;int&gt;</c> via
/// recursive descent, so nested cases like
/// <c>IList&lt;IList&lt;string&gt;&gt;</c> work without per-call
/// reflection plumbing at the call site.
/// </para>
/// <para>
/// <b>Two-pass cost.</b> The wire-decode pass produces a canonical
/// tree; this adapter walks that tree a second time. Every level is
/// touched twice. Tolerable at MVP data scale; if profiling exposes
/// the cost the alternative is plumbing a <see cref="System.Type"/>
/// hint down through <see cref="SerializationRegistry.ReadObject"/> so
/// the converter materialises target-shaped in one pass. That retrofit
/// would not break public surface — only <see cref="Convert{T}"/>
/// callers would shift internally.
/// </para>
/// <para>
/// <b>Null handling.</b> <c>null</c> input becomes <c>default(T)</c>:
/// reference types fall through as <c>null</c>, value types collapse to
/// their zero value (<c>0</c>, <c>false</c>, etc.). Matches the .NET
/// convention for "no value" returns in dictionary-style APIs.
/// </para>
/// <para>
/// <b>Early out.</b> Scalars (<c>int</c>, <c>string</c>) and primitive
/// arrays (<c>int[]</c>, <c>string[]</c>) come back from the registry
/// already in their concrete CLR type, so the <see cref="Type.IsInstanceOfType"/>
/// check returns the input unchanged with zero allocation.
/// </para>
/// </remarks>
internal sealed class TypedResultAdapter
{
    /// <summary>
    /// Typed entry — convert <paramref name="raw"/> to the
    /// <typeparamref name="T"/> shape. <c>null</c> input yields
    /// <c>default(T)</c>.
    /// </summary>
    public T? Convert<T>(object? raw)
    {
        if (raw is null)
        {
            return default;
        }
        // The non-generic worker has already produced a value
        // assignable to typeof(T); the cast unboxes value types and
        // is a reference cast otherwise.
        return (T?)Convert(raw, typeof(T));
    }

    /// <summary>
    /// Reflection entry — convert <paramref name="raw"/> to a value
    /// assignable to <paramref name="targetType"/>. Recurses into
    /// generic container element types.
    /// </summary>
    /// <exception cref="InvalidCastException">
    /// <paramref name="raw"/>'s shape cannot be reshaped to
    /// <paramref name="targetType"/> (e.g. raw is a <c>string</c> and
    /// the target is <c>int[]</c>), or <paramref name="targetType"/> is
    /// a generic family the adapter does not yet know about (Dictionary
    /// / HashSet land in follow-up PRs).
    /// </exception>
    public object? Convert(object? raw, Type targetType)
    {
        if (raw is null)
        {
            return null;
        }

        // Scalars and primitive arrays already arrive typed from the
        // registry — int, string, bool[], string[], …
        if (targetType.IsInstanceOfType(raw))
        {
            return raw;
        }

        if (targetType.IsGenericType)
        {
            var def = targetType.GetGenericTypeDefinition();

            // IList<T> / List<T> / IEnumerable<T> / ICollection<T> /
            // IReadOnlyList<T> / IReadOnlyCollection<T> — all
            // assignable from List<T>, so a single materialisation
            // covers them.
            if (def == typeof(IList<>) || def == typeof(List<>)
             || def == typeof(IEnumerable<>) || def == typeof(ICollection<>)
             || def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>))
            {
                return ConvertToList(raw, targetType.GetGenericArguments()[0]);
            }

            // ISet<T> / HashSet<T> / IReadOnlySet<T> — all assignable
            // from HashSet<T>; canonical raw is HashSet<object?> from
            // HashSetDataConverter.
            if (def == typeof(ISet<>) || def == typeof(HashSet<>)
             || def == typeof(IReadOnlySet<>))
            {
                return ConvertToHashSet(raw, targetType.GetGenericArguments()[0]);
            }

            // IDictionary<K,V> / Dictionary<K,V> /
            // IReadOnlyDictionary<K,V> — all assignable from
            // Dictionary<K,V>; canonical raw is
            // Dictionary<object, object?> from DictionaryDataConverter.
            if (def == typeof(IDictionary<,>) || def == typeof(Dictionary<,>)
             || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = targetType.GetGenericArguments();
                return ConvertToDictionary(raw, args[0], args[1]);
            }

            // LinkedList<T> — own branch because LinkedList<T> does
            // NOT implement IList<T>; it's a peer of HashSet<T> on
            // the .NET collection-interface lattice.
            if (def == typeof(LinkedList<>))
            {
                return ConvertToLinkedList(raw, targetType.GetGenericArguments()[0]);
            }

            // Stack<T> — own branch because the canonical
            // Stack<object?> iterates top→bottom and Stack<T>'s
            // IEnumerable ctor pushes in iteration order, so a naïve
            // pass-through would invert the stack. See ConvertToStack
            // for the reverse step.
            if (def == typeof(Stack<>))
            {
                return ConvertToStack(raw, targetType.GetGenericArguments()[0]);
            }

            // Follow-up PRs:
            //   Queue<T>
            //   SortedSet<T>, SortedDictionary<,>

            throw new InvalidCastException(
                $"TypedResultAdapter has no rule for generic target {targetType}.");
        }

        if (targetType.IsArray)
        {
            return ConvertToArray(raw, targetType.GetElementType()!);
        }

        throw new InvalidCastException(
            $"TypedResultAdapter cannot convert {raw.GetType()} to {targetType}.");
    }

    /// <summary>
    /// Build a <c>List&lt;<paramref name="elementType"/>&gt;</c> from
    /// any enumerable <paramref name="raw"/>, recursing per element so
    /// nested generics (<c>IList&lt;IList&lt;string&gt;&gt;</c>) line
    /// up.
    /// </summary>
    private object ConvertToList(object raw, Type elementType)
    {
        if (raw is not IEnumerable source)
        {
            throw new InvalidCastException(
                $"TypedResultAdapter: expected an enumerable to materialise a list, got {raw.GetType()}.");
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in source)
        {
            list.Add(Convert(item, elementType));
        }
        return list;
    }

    /// <summary>
    /// Build a <c>HashSet&lt;<paramref name="elementType"/>&gt;</c>
    /// from any enumerable <paramref name="raw"/>. Materialises the
    /// converted elements into a typed <c>List&lt;elementType&gt;</c>
    /// first, then constructs the set from <c>HashSet&lt;T&gt;</c>'s
    /// <c>IEnumerable&lt;T&gt;</c> constructor — keeps the recursive
    /// element conversion path identical to the list branch and avoids
    /// reflecting on <c>HashSet&lt;T&gt;.Add</c>.
    /// </summary>
    private object ConvertToHashSet(object raw, Type elementType)
    {
        if (raw is not IEnumerable source)
        {
            throw new InvalidCastException(
                $"TypedResultAdapter: expected an enumerable to materialise a set, got {raw.GetType()}.");
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in source)
        {
            list.Add(Convert(item, elementType));
        }

        // HashSet<T>(IEnumerable<T>) ctor — picked over CreateInstance
        // + reflective Add so element conversion stays uniform with
        // ConvertToList.
        var setType = typeof(HashSet<>).MakeGenericType(elementType);
        return Activator.CreateInstance(setType, list)!;
    }

    /// <summary>
    /// Build a
    /// <c>Dictionary&lt;<paramref name="keyType"/>,<paramref name="valueType"/>&gt;</c>
    /// from any non-generic <see cref="IDictionary"/>
    /// <paramref name="raw"/>. Each entry's key and value run through
    /// <see cref="Convert(object?, Type)"/> independently so nested
    /// generics on either side line up.
    /// </summary>
    private object ConvertToDictionary(object raw, Type keyType, Type valueType)
    {
        if (raw is not IDictionary source)
        {
            throw new InvalidCastException(
                $"TypedResultAdapter: expected a dictionary to materialise a map, got {raw.GetType()}.");
        }

        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        var dict = (IDictionary)Activator.CreateInstance(dictType, source.Count)!;
        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is null)
            {
                // DictionaryDataConverter.Read already filters this on
                // the way in, but a non-wire-origin raw (e.g. a unit
                // test feeding a hand-built dictionary) could still
                // carry one. Surface the same shape of failure.
                throw new InvalidCastException(
                    "TypedResultAdapter: source dictionary contains a null key; "
                    + "Dictionary<TKey,TValue> does not permit null keys.");
            }

            dict.Add(
                Convert(entry.Key, keyType)!,
                Convert(entry.Value, valueType));
        }
        return dict;
    }

    /// <summary>
    /// Build a <c>LinkedList&lt;<paramref name="elementType"/>&gt;</c>
    /// from any enumerable <paramref name="raw"/>. Same recipe as
    /// <see cref="ConvertToHashSet"/>: materialise typed elements into
    /// a scratch <c>List&lt;elementType&gt;</c> first, then construct
    /// the linked list from its
    /// <c>IEnumerable&lt;T&gt;</c> ctor — preserves source iteration
    /// order, which for canonical <c>LinkedList&lt;object?&gt;</c>
    /// from the wire is head→tail.
    /// </summary>
    private object ConvertToLinkedList(object raw, Type elementType)
    {
        if (raw is not IEnumerable source)
        {
            throw new InvalidCastException(
                $"TypedResultAdapter: expected an enumerable to materialise a linked list, got {raw.GetType()}.");
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in source)
        {
            list.Add(Convert(item, elementType));
        }

        // LinkedList<T>(IEnumerable<T>) ctor appends each element via
        // AddLast — source iteration order becomes head→tail.
        var llType = typeof(LinkedList<>).MakeGenericType(elementType);
        return Activator.CreateInstance(llType, list)!;
    }

    /// <summary>
    /// Build a <c>Stack&lt;<paramref name="elementType"/>&gt;</c> from
    /// any enumerable <paramref name="raw"/>. Source iteration is
    /// assumed top→bottom (canonical <c>Stack&lt;object?&gt;</c> from
    /// <see cref="StackDataConverter.Read"/> works that way); the
    /// helper reverses before constructing the typed stack so its
    /// <c>IEnumerable&lt;T&gt;</c> ctor (which pushes in iteration
    /// order) ends up with the original top still on top.
    /// </summary>
    private object ConvertToStack(object raw, Type elementType)
    {
        if (raw is not IEnumerable source)
        {
            throw new InvalidCastException(
                $"TypedResultAdapter: expected an enumerable to materialise a stack, got {raw.GetType()}.");
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in source)
        {
            list.Add(Convert(item, elementType));
        }

        // In-place reverse at the non-generic IList layer — avoids
        // reflecting on List<T>.Reverse() while still being O(N).
        // After reverse: list[0] is the original bottom, list[N-1]
        // is the original top. Stack<T>(IEnumerable<T>) pushes in
        // that order, so the rebuilt stack has the original top on
        // top.
        for (int i = 0, j = list.Count - 1; i < j; i++, j--)
        {
            (list[i], list[j]) = (list[j], list[i]);
        }

        var stackType = typeof(Stack<>).MakeGenericType(elementType);
        return Activator.CreateInstance(stackType, list)!;
    }

    /// <summary>
    /// Build a typed <see cref="Array"/> of
    /// <paramref name="elementType"/> from any collection
    /// <paramref name="raw"/> with a known length.
    /// </summary>
    private object ConvertToArray(object raw, Type elementType)
    {
        if (raw is not ICollection source)
        {
            throw new InvalidCastException(
                $"TypedResultAdapter: expected a collection to materialise an array, got {raw.GetType()}.");
        }

        var array = Array.CreateInstance(elementType, source.Count);
        var i = 0;
        foreach (var item in source)
        {
            array.SetValue(Convert(item, elementType), i++);
        }
        return array;
    }
}
