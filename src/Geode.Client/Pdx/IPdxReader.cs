namespace Geode.Client.Pdx;

/// <summary>Reads PDX fields during deserialization.</summary>
public interface IPdxReader
{
    /// <summary>Read a <see cref="bool"/> field.</summary>
    bool ReadBoolean(string fieldName);

    /// <summary>Read a signed 8-bit field (Java <c>byte</c>).</summary>
    sbyte ReadByte(string fieldName);

    /// <summary>Read an unsigned 16-bit char field (Java <c>char</c>).</summary>
    char ReadChar(string fieldName);

    /// <summary>Read a signed 16-bit field (Java <c>short</c>).</summary>
    short ReadShort(string fieldName);

    /// <summary>Read a signed 32-bit field (Java <c>int</c>).</summary>
    int ReadInt(string fieldName);

    /// <summary>Read a signed 64-bit field (Java <c>long</c>).</summary>
    long ReadLong(string fieldName);

    /// <summary>Read a single-precision float field.</summary>
    float ReadFloat(string fieldName);

    /// <summary>Read a double-precision float field.</summary>
    double ReadDouble(string fieldName);

    /// <summary>Read a string field.</summary>
    string? ReadString(string fieldName);

    /// <summary>Read a date field (Java <c>java.util.Date</c>).</summary>
    DateTime ReadDate(string fieldName);
}
