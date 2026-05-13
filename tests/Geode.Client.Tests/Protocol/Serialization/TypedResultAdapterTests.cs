using Geode.Client.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class TypedResultAdapterTests
{
    private readonly TypedResultAdapter _adapter = new();

    // ── Null handling ──────────────────────────────────────────

    [Fact]
    public void Convert_null_returns_null_for_reference_target()
    {
        Assert.Null(_adapter.Convert<string>(null));
        Assert.Null(_adapter.Convert<List<int>>(null));
        Assert.Null(_adapter.Convert<int[]>(null));
    }

    [Fact]
    public void Convert_null_returns_default_for_value_target()
    {
        // Matches IDictionary<TKey,TValue>.TryGetValue semantics:
        // missing → default(TValue). 0 for int, false for bool.
        Assert.Equal(0, _adapter.Convert<int>(null));
        Assert.False(_adapter.Convert<bool>(null));
    }

    [Fact]
    public void Convert_non_generic_null_returns_null()
    {
        Assert.Null(_adapter.Convert(null, typeof(string)));
        Assert.Null(_adapter.Convert(null, typeof(int)));
    }

    // ── Early out (IsInstanceOfType) ───────────────────────────

    [Fact]
    public void Convert_scalar_already_target_type_passes_through()
    {
        Assert.Equal(42, _adapter.Convert<int>(42));
        Assert.Equal("hello", _adapter.Convert<string>("hello"));
        Assert.True(_adapter.Convert<bool>(true));
    }

    [Fact]
    public void Convert_primitive_array_already_target_type_passes_through()
    {
        var arr = new[] { 1, 2, 3 };
        Assert.Same(arr, _adapter.Convert<int[]>(arr));
    }

    [Fact]
    public void Convert_concrete_list_already_assignable_passes_through()
    {
        // List<int> IS-A IList<int> → IsInstanceOfType early-out, no
        // allocation, same reference returned.
        var list = new List<int> { 1, 2, 3 };
        Assert.Same(list, _adapter.Convert<List<int>>(list));
        Assert.Same(list, _adapter.Convert<IList<int>>(list));
        Assert.Same(list, _adapter.Convert<IEnumerable<int>>(list));
    }

    // ── IList<T> family ────────────────────────────────────────

    [Fact]
    public void Convert_canonical_to_IList_int()
    {
        var raw = new List<object?> { 1, 2, 3 };
        var result = _adapter.Convert<IList<int>>(raw);
        Assert.IsType<List<int>>(result);
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void Convert_canonical_to_List_string()
    {
        var raw = new List<object?> { "a", "b" };
        var result = _adapter.Convert<List<string>>(raw);
        Assert.Equal(new[] { "a", "b" }, result);
    }

    [Theory]
    [InlineData(typeof(IList<int>))]
    [InlineData(typeof(List<int>))]
    [InlineData(typeof(IEnumerable<int>))]
    [InlineData(typeof(ICollection<int>))]
    [InlineData(typeof(IReadOnlyList<int>))]
    [InlineData(typeof(IReadOnlyCollection<int>))]
    public void Convert_materialises_List_T_for_every_supported_list_shape(Type targetType)
    {
        // All six target shapes converge on List<T> as the materialised
        // form — assignment compatibility lines up for each.
        var raw = new List<object?> { 1, 2, 3 };
        var result = _adapter.Convert(raw, targetType);
        Assert.NotNull(result);
        Assert.IsType<List<int>>(result);
        Assert.True(targetType.IsInstanceOfType(result));
    }

    // ── Nested ─────────────────────────────────────────────────

    [Fact]
    public void Convert_nested_IList_of_IList_string()
    {
        var raw = new List<object?>
        {
            new List<object?> { "a", "b" },
            new List<object?> { "c" },
        };
        var result = _adapter.Convert<IList<IList<string>>>(raw);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal(new[] { "a", "b" }, result[0]);
        Assert.Equal(new[] { "c" }, result[1]);
    }

    [Fact]
    public void Convert_three_level_nested_IList()
    {
        var raw = new List<object?>
        {
            new List<object?>
            {
                new List<object?> { 1 },
                new List<object?> { 2, 3 },
            },
        };
        var result = _adapter.Convert<IList<IList<IList<int>>>>(raw);
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(2, result![0].Count);
        Assert.Equal(new[] { 1 }, result[0][0]);
        Assert.Equal(new[] { 2, 3 }, result[0][1]);
    }

    [Fact]
    public void Convert_nested_null_element_stays_null()
    {
        var raw = new List<object?>
        {
            new List<object?> { "a" },
            null,
        };
        var result = _adapter.Convert<IList<IList<string>>>(raw);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal(new[] { "a" }, result[0]);
        Assert.Null(result[1]);
    }

    // ── Arrays ─────────────────────────────────────────────────

    [Fact]
    public void Convert_canonical_to_int_array()
    {
        var raw = new List<object?> { 1, 2, 3 };
        var result = _adapter.Convert<int[]>(raw);
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void Convert_canonical_to_string_array()
    {
        var raw = new List<object?> { "a", "b" };
        var result = _adapter.Convert<string[]>(raw);
        Assert.Equal(new[] { "a", "b" }, result);
    }

    [Fact]
    public void Convert_empty_canonical_to_int_array()
    {
        var raw = new List<object?>();
        var result = _adapter.Convert<int[]>(raw);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    // ── Failure modes ──────────────────────────────────────────

    [Fact]
    public void Convert_unknown_generic_target_throws()
    {
        // Dictionary<,> is a known unknown — adapter does not yet have
        // a branch for it (follow-up PR).
        var raw = new List<object?> { 1, 2 };
        Assert.Throws<InvalidCastException>(
            () => _adapter.Convert<Dictionary<int, string>>(raw));
    }

    [Fact]
    public void Convert_scalar_to_list_target_throws()
    {
        // raw is not enumerable — can't be materialised as a list.
        Assert.Throws<InvalidCastException>(
            () => _adapter.Convert<IList<int>>(42));
    }

    [Fact]
    public void Convert_scalar_to_array_target_throws()
    {
        Assert.Throws<InvalidCastException>(
            () => _adapter.Convert<int[]>("not an enumerable"));
    }
}
