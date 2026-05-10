namespace Geode.Client;

public interface IRegion
{
}

public interface IRegion<TKey, TValue> : IRegion
    where TKey : notnull
{
}
