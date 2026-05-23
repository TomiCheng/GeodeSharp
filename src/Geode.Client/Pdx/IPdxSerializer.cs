/*
namespace Geode.Client.Pdx;

/// <summary>External PDX serializer for types that can't (or shouldn't) implement <see cref="IPdxSerializable{TSelf}"/>.</summary>
public interface IPdxSerializer<T>
{
    /// <summary>Serialize <paramref name="obj"/>'s fields.</summary>
    void ToData(T obj, IPdxWriter writer);

    /// <summary>Reconstruct a <typeparamref name="T"/> instance from the reader.</summary>
    T FromData(IPdxReader reader);
}

*/