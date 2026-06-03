namespace Geode.Client.Internal;

internal static class TSSTXStateWrapper
{
    private static readonly AsyncLocal<TXState?> _current = new();

    public static TXState? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
