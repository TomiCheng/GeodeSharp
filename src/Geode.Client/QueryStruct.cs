using System.Collections;

namespace Geode.Client;

/// <summary>
/// A row of a multi-column OQL projection query
/// (<c>SELECT field1, field2 FROM /region</c>)
/// </summary>
public sealed class QueryStruct : IReadOnlyList<object?>
{
    private readonly IReadOnlyList<object?> _values;
    private readonly Dictionary<string, int> _fieldIndex;

    public QueryStruct(IReadOnlyList<string> fieldNames, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        ArgumentNullException.ThrowIfNull(values);
        if (fieldNames.Count != values.Count)
        {
            throw new ArgumentException(
                $"QueryStruct field count mismatch: {fieldNames.Count} names vs {values.Count} values.");
        }

        FieldNames = fieldNames;
        _values = values;

        // Build name → index map. Field names in OQL projection are
        // case-sensitive on the server, so ordinal comparison here.
        _fieldIndex = new Dictionary<string, int>(fieldNames.Count, StringComparer.Ordinal);
        for (var i = 0; i < fieldNames.Count; i++)
        {
            _fieldIndex[fieldNames[i]] = i;
        }
    }

    /// <summary>Ordered field names from the OQL projection.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    /// <summary>Number of fields in this row.</summary>
    public int Count => _values.Count;

    /// <summary>Value at <paramref name="index"/>.</summary>
    public object? this[int index] => _values[index];

    /// <summary>Value of the field named <paramref name="fieldName"/>.</summary>
    /// <exception cref="KeyNotFoundException">
    /// <paramref name="fieldName"/> is not in <see cref="FieldNames"/>.
    /// </exception>
    public object? this[string fieldName] => _values[GetFieldIndex(fieldName)];

    /// <summary>Index of the field named <paramref name="fieldName"/>.</summary>
    /// <exception cref="KeyNotFoundException">
    /// <paramref name="fieldName"/> is not in <see cref="FieldNames"/>.
    /// </exception>
    public int GetFieldIndex(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        if (!_fieldIndex.TryGetValue(fieldName, out var idx))
        {
            throw new KeyNotFoundException(
                $"QueryStruct has no field named '{fieldName}'.");
        }
        return idx;
    }

    /// <summary>Name of the field at <paramref name="index"/>.</summary>
    public string GetFieldName(int index) => FieldNames[index];

    public IEnumerator<object?> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
