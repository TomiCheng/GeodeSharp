using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class SerializationRegistryTests
{
    [Fact]
    public void WriteObject_dispatches_closed_List_int_via_open_generic_fallback()
    {
        // List<int> is not registered as a closed type in _byType; the
        // dispatch falls back to GetGenericTypeDefinition() which hits
        // _byType[typeof(List<>)] = ListDataConverter.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableArrayList, 0x01,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x07,
            },
            SerializationTestHelpers.Encode(new List<int> { 7 }));
    }

    [Fact]
    public void WriteObject_dispatches_closed_List_string_via_same_open_generic()
    {
        // Same converter instance handles every closed List<T> — the
        // open-generic registration is shared.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableArrayList, 0x01,
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x58,    // "X"
            },
            SerializationTestHelpers.Encode(new List<string> { "X" }));
    }

    [Fact]
    public void WriteObject_unregistered_closed_generic_still_throws_NotSupported()
    {
        // SortedDictionary<,> has no registered converter — open-
        // generic fallback probes typeof(SortedDictionary<,>), misses
        // even though Dictionary<,> IS registered (different open-
        // generic identity), and the unregistered-type branch fires.
        Assert.Throws<NotSupportedException>(
            () => SerializationTestHelpers.Encode(new SortedDictionary<int, string>()));
    }

    [Fact]
    public void WriteObject_existing_scalar_path_unchanged_by_open_generic_fallback()
    {
        // Regression guard: scalars (non-generic types) still hit
        // _byType on the first lookup; fallback only triggers when
        // type.IsGenericType.
        Assert.Equal(
            new byte[] { DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x2A },
            SerializationTestHelpers.Encode(42));
    }

    [Fact]
    public void WriteObject_existing_array_path_unchanged_by_open_generic_fallback()
    {
        // int[] is a concrete (non-generic) type registered in _byType
        // directly — closed lookup hits without touching the fallback.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableInt32Array, 0x01,
                0x00, 0x00, 0x00, 0x07,
            },
            SerializationTestHelpers.Encode(new[] { 7 }));
    }
}
