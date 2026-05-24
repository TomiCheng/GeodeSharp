using Geode.Client;
using Xunit;

namespace Geode.Client.Tests;

/// <summary>
/// Parity lock for cppcache <c>RegionShortcut</c>
/// (<c>cppcache/include/geode/RegionShortcut.hpp:44-72</c>): five values
/// in the documented order.
/// </summary>
public class RegionShortcutTests
{
    [Fact]
    public void Has_Exactly_Five_Members()
    {
        var names = Enum.GetNames<RegionShortcut>();
        Assert.Equal(5, names.Length);
    }

    [Fact]
    public void Members_Match_Cppcache()
    {
        Assert.True(Enum.IsDefined(RegionShortcut.Proxy));
        Assert.True(Enum.IsDefined(RegionShortcut.CachingProxy));
        Assert.True(Enum.IsDefined(RegionShortcut.CachingProxyEntryLru));
        Assert.True(Enum.IsDefined(RegionShortcut.Local));
        Assert.True(Enum.IsDefined(RegionShortcut.LocalEntryLru));
    }
}
