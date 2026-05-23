/*
using Xunit;

namespace Geode.Client.Tests;

public class QueryExtensionsTests
{
    // ====================================================================
    //  In-memory IQuery<T> for testing the extensions in isolation —
    //  ExecuteAsync just returns whatever rows the test sets.
    // ====================================================================
    private sealed class FakeQuery<T> : IQuery<T>
    {
        public string QueryString { get; set; } = "SELECT * FROM /test";
        public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public IList<object?> Parameters { get; } = [];

        public IReadOnlyList<T> Rows { get; set; } = [];

        public Task<IReadOnlyList<T>> ExecuteAsync(CancellationToken ct = default)
            => Task.FromResult(Rows);
    }

    // ====================================================================
    //  ExecuteSingleAsync
    // ====================================================================

    [Fact]
    public async Task ExecuteSingleAsync_returns_only_row()
    {
        var q = new FakeQuery<int> { Rows = [42] };
        Assert.Equal(42, await q.ExecuteSingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteSingleAsync_throws_when_empty()
    {
        var q = new FakeQuery<int> { Rows = [] };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            q.ExecuteSingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteSingleAsync_throws_when_multiple()
    {
        var q = new FakeQuery<int> { Rows = [1, 2] };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            q.ExecuteSingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteSingleAsync_rejects_null_query()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            QueryExtensions.ExecuteSingleAsync<int>(null!, TestContext.Current.CancellationToken));
    }

    // ====================================================================
    //  ExecuteFirstOrDefaultAsync
    // ====================================================================

    [Fact]
    public async Task ExecuteFirstOrDefaultAsync_returns_first_row()
    {
        var q = new FakeQuery<int> { Rows = [1, 2, 3] };
        Assert.Equal(1, await q.ExecuteFirstOrDefaultAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteFirstOrDefaultAsync_returns_default_when_empty_value_type()
    {
        var q = new FakeQuery<int> { Rows = [] };
        Assert.Equal(0, await q.ExecuteFirstOrDefaultAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteFirstOrDefaultAsync_returns_null_when_empty_reference_type()
    {
        var q = new FakeQuery<string> { Rows = [] };
        Assert.Null(await q.ExecuteFirstOrDefaultAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteFirstOrDefaultAsync_rejects_null_query()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            QueryExtensions.ExecuteFirstOrDefaultAsync<int>(null!, TestContext.Current.CancellationToken));
    }

    // ====================================================================
    //  WithParameters
    // ====================================================================

    [Fact]
    public void WithParameters_sets_each_value_in_order()
    {
        var q = new FakeQuery<int>();
        q.WithParameters(100, "PAID");
        Assert.Equal(new object?[] { 100, "PAID" }, q.Parameters);
    }

    [Fact]
    public void WithParameters_replaces_existing_values()
    {
        var q = new FakeQuery<int>();
        q.Parameters.Add(999);
        q.WithParameters(1, 2);
        Assert.Equal(new object?[] { 1, 2 }, q.Parameters);
    }

    [Fact]
    public void WithParameters_with_no_args_clears()
    {
        var q = new FakeQuery<int>();
        q.Parameters.Add(7);
        q.WithParameters();
        Assert.Empty(q.Parameters);
    }

    [Fact]
    public void WithParameters_returns_same_instance_for_chaining()
    {
        var q = new FakeQuery<int>();
        Assert.Same(q, q.WithParameters(1));
    }

    [Fact]
    public void WithParameters_preserves_null_entries()
    {
        var q = new FakeQuery<int>();
        q.WithParameters(1, null, 3);
        Assert.Equal(new object?[] { 1, null, 3 }, q.Parameters);
    }

    [Fact]
    public void WithParameters_rejects_null_query()
    {
        Assert.Throws<ArgumentNullException>(() =>
            QueryExtensions.WithParameters<int>(null!, 1));
    }

    // ====================================================================
    //  WithResponseTimeout
    // ====================================================================

    [Fact]
    public void WithResponseTimeout_sets_property()
    {
        var q = new FakeQuery<int>();
        q.WithResponseTimeout(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), q.ResponseTimeout);
    }

    [Fact]
    public void WithResponseTimeout_returns_same_instance_for_chaining()
    {
        var q = new FakeQuery<int>();
        Assert.Same(q, q.WithResponseTimeout(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void WithResponseTimeout_rejects_null_query()
    {
        Assert.Throws<ArgumentNullException>(() =>
            QueryExtensions.WithResponseTimeout<int>(null!, TimeSpan.FromSeconds(1)));
    }

    // ====================================================================
    //  Fluent chain — multiple extensions composed
    // ====================================================================

    [Fact]
    public async Task Fluent_chain_returns_typed_single_value()
    {
        var q = new FakeQuery<int> { Rows = [42] };
        var result = await q.WithParameters(100)
                            .WithResponseTimeout(TimeSpan.FromSeconds(30))
                            .ExecuteSingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(42, result);
        Assert.Equal(new object?[] { 100 }, q.Parameters);
        Assert.Equal(TimeSpan.FromSeconds(30), q.ResponseTimeout);
    }
}

*/