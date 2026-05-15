using Xunit;

namespace Geode.Client.Tests;

public class QueryStructTests
{
    private static QueryStruct TwoField(string id = "1", string name = "x") =>
        new(new[] { "id", "name" }, new object?[] { id, name });

    // ====================================================================
    //  Construction
    // ====================================================================

    [Fact]
    public void Ctor_rejects_null_field_names()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new QueryStruct(null!, new object?[] { 1 }));
    }

    [Fact]
    public void Ctor_rejects_null_values()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new QueryStruct(new[] { "x" }, null!));
    }

    [Fact]
    public void Ctor_rejects_count_mismatch()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new QueryStruct(new[] { "id", "name" }, new object?[] { 1 }));
        Assert.Contains("field count mismatch", ex.Message);
    }

    [Fact]
    public void Ctor_accepts_zero_field_zero_value()
    {
        var s = new QueryStruct([], []);
        Assert.Empty(s);
    }

    // ====================================================================
    //  FieldNames / Count
    // ====================================================================

    [Fact]
    public void FieldNames_returns_supplied_names_in_order()
    {
        var s = TwoField();
        Assert.Equal(new[] { "id", "name" }, s.FieldNames);
    }

    [Fact]
    public void Count_equals_field_count()
    {
        Assert.Equal(2, TwoField().Count);
    }

    // ====================================================================
    //  Indexers
    // ====================================================================

    [Fact]
    public void Indexer_by_int_returns_value_at_position()
    {
        var s = TwoField("42", "alice");
        Assert.Equal("42", s[0]);
        Assert.Equal("alice", s[1]);
    }

    [Fact]
    public void Indexer_by_name_returns_value_for_field()
    {
        var s = TwoField("42", "alice");
        Assert.Equal("42", s["id"]);
        Assert.Equal("alice", s["name"]);
    }

    [Fact]
    public void Indexer_by_name_throws_on_unknown_field()
    {
        Assert.Throws<KeyNotFoundException>(() => _ = TwoField()["missing"]);
    }

    [Fact]
    public void Indexer_by_name_is_case_sensitive()
    {
        // Geode OQL field names are case-sensitive on the server; client
        // matches via StringComparer.Ordinal.
        Assert.Throws<KeyNotFoundException>(() => _ = TwoField()["ID"]);
    }

    [Fact]
    public void Indexer_by_int_out_of_range_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = TwoField()[5]);
    }

    // ====================================================================
    //  GetFieldIndex / GetFieldName
    // ====================================================================

    [Fact]
    public void GetFieldIndex_returns_zero_based_position()
    {
        var s = TwoField();
        Assert.Equal(0, s.GetFieldIndex("id"));
        Assert.Equal(1, s.GetFieldIndex("name"));
    }

    [Fact]
    public void GetFieldIndex_throws_on_unknown_field()
    {
        Assert.Throws<KeyNotFoundException>(() => TwoField().GetFieldIndex("ghost"));
    }

    [Fact]
    public void GetFieldName_returns_name_at_index()
    {
        var s = TwoField();
        Assert.Equal("id", s.GetFieldName(0));
        Assert.Equal("name", s.GetFieldName(1));
    }

    // ====================================================================
    //  IReadOnlyList<object?> iteration
    // ====================================================================

    [Fact]
    public void Enumeration_yields_values_in_field_order()
    {
        var s = TwoField("42", "alice");
        Assert.Equal(new object?[] { "42", "alice" }, s.ToArray());
    }

    [Fact]
    public void Null_field_value_is_preserved()
    {
        var s = new QueryStruct(new[] { "a", "b" }, new object?[] { null, "x" });
        Assert.Null(s[0]);
        Assert.Null(s["a"]);
        Assert.Equal("x", s["b"]);
    }
}
