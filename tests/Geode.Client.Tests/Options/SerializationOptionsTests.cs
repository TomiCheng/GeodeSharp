using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options;

public class SerializationOptionsTests
{
    // ── Clone ─────────────────────────────────────────────────

    [Fact]
    public void Clone_copies_values()
    {
        var original = new SerializationOptions
        {
            MaxDepth = 32,
            MaxArrayLength = 500_000,
            MaxBytesLength = 5_000_000,
            MaxStringLength = 250_000,
        };
        var clone = original.Clone();

        Assert.Equal(32, clone.MaxDepth);
        Assert.Equal(500_000, clone.MaxArrayLength);
        Assert.Equal(5_000_000, clone.MaxBytesLength);
        Assert.Equal(250_000, clone.MaxStringLength);
    }

    [Fact]
    public void Clone_mutating_clone_does_not_affect_original()
    {
        var original = new SerializationOptions { MaxDepth = 32 };
        var clone = original.Clone();

        clone.MaxDepth = 999;

        Assert.Equal(32, original.MaxDepth);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_defaults_pass()
    {
        Assert.Empty(new SerializationOptions().Validate("s"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_MaxDepth_below_one_fails(int depth)
    {
        var opts = new SerializationOptions { MaxDepth = depth };
        var failures = opts.Validate("s").ToList();
        Assert.Contains(failures, f => f.Contains("s.MaxDepth") && f.Contains(depth.ToString()));
    }

    [Fact]
    public void Validate_MaxDepth_one_passes()
    {
        // Boundary: 1 is the minimum legal value.
        var opts = new SerializationOptions { MaxDepth = 1 };
        Assert.Empty(opts.Validate("s"));
    }

    [Theory]
    [InlineData(nameof(SerializationOptions.MaxArrayLength))]
    [InlineData(nameof(SerializationOptions.MaxBytesLength))]
    [InlineData(nameof(SerializationOptions.MaxStringLength))]
    public void Validate_lengths_negative_fail(string propName)
    {
        var opts = new SerializationOptions();
        switch (propName)
        {
            case nameof(SerializationOptions.MaxArrayLength): opts.MaxArrayLength = -1; break;
            case nameof(SerializationOptions.MaxBytesLength): opts.MaxBytesLength = -1; break;
            case nameof(SerializationOptions.MaxStringLength): opts.MaxStringLength = -1; break;
        }

        var failures = opts.Validate("s").ToList();
        Assert.Contains(failures, f => f.Contains($"s.{propName}"));
    }

    [Theory]
    [InlineData(nameof(SerializationOptions.MaxArrayLength))]
    [InlineData(nameof(SerializationOptions.MaxBytesLength))]
    [InlineData(nameof(SerializationOptions.MaxStringLength))]
    public void Validate_lengths_zero_pass(string propName)
    {
        // Boundary: 0 is legal (only empty payloads accepted).
        var opts = new SerializationOptions();
        switch (propName)
        {
            case nameof(SerializationOptions.MaxArrayLength): opts.MaxArrayLength = 0; break;
            case nameof(SerializationOptions.MaxBytesLength): opts.MaxBytesLength = 0; break;
            case nameof(SerializationOptions.MaxStringLength): opts.MaxStringLength = 0; break;
        }

        Assert.Empty(opts.Validate("s"));
    }
}
