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

            // Follow-up PRs:
            //   IDictionary<,> / Dictionary<,> / IReadOnlyDictionary<,>
            //   ISet<T> / HashSet<T>
            //   LinkedList<T>, Stack<T>, Queue<T>

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
