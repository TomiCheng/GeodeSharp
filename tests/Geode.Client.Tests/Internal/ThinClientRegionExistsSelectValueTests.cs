using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for the OQL-backed region convenience methods
/// <see cref="ThinClientRegion.ExistsValueAsync"/> /
/// <see cref="ThinClientRegion.SelectValueAsync"/>. The wire path itself
/// (RemoteQueryService → RemoteQuery → MessageType.Query/QueryWithParameters
/// → ChunkedQueryResponse) needs a real <see cref="ThinClientPoolDM"/>;
/// success-path coverage is in the integration test. This file locks the
/// input-validation + non-pool-DM guard that fires before any wire
/// machinery.
/// </summary>
public class ThinClientRegionExistsSelectValueTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private static GeodeCache MakeCache(IServiceProvider sp) =>
        ActivatorUtilities.CreateInstance<GeodeCache>(sp, "c");

    private static ThinClientRegion MakeRegion(
        IServiceProvider sp, GeodeCache cache, FakeThinClientBaseDM dm) =>
        new(sp, NullLogger<ThinClientRegion>.Instance, "orders", new RegionAttributes(), dm);

    // ── Predicate validation: empty / whitespace ──────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task ExistsValueAsync_EmptyOrWhitespacePredicate_ThrowsArgumentException(string predicate)
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache);
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentException>(
            () => region.ExistsValueAsync(predicate, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task SelectValueAsync_EmptyOrWhitespacePredicate_ThrowsArgumentException(string predicate)
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache);
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentException>(
            () => region.SelectValueAsync(predicate, TestContext.Current.CancellationToken));
    }

    // ── Non-pool DM guard ─────────────────────────────────────────

    [Fact]
    public async Task ExistsValueAsync_NonPoolDM_ThrowsNotImplementedException()
    {
        // FakeThinClientBaseDM is the abstract base, not ThinClientPoolDM,
        // so QueryAsync's `dm is not ThinClientPoolDM` guard fires.
        // Memory pool-only-no-non-pool.md: non-pool DM routing deferred.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache);
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<NotImplementedException>(
            () => region.ExistsValueAsync("this='x'", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SelectValueAsync_NonPoolDM_ThrowsNotImplementedException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache);
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<NotImplementedException>(
            () => region.SelectValueAsync("this='x'", TestContext.Current.CancellationToken));
    }
}
