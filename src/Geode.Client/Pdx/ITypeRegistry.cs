namespace Geode.Client.Pdx;

/// <summary>Per-cache PDX type registry; accessed via <see cref="IGeodeCache.TypeRegistry"/>.</summary>
public interface ITypeRegistry
{
    /// <summary>Register an intrusive PDX type; <paramref name="className"/> defaults to <c>typeof(T).FullName</c> for cross-language identity.</summary>
    void RegisterPdxType<T>(string? className = null) where T : IPdxSerializable<T>;

    /// <summary>Register an external PDX serializer for <typeparamref name="T"/>; <paramref name="className"/> defaults to <c>typeof(T).FullName</c>.</summary>
    void RegisterPdxSerializer<T>(IPdxSerializer<T> serializer, string? className = null);
}
