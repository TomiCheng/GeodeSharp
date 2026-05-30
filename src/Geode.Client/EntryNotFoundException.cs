namespace Geode.Client;

/// <summary>
/// Thrown by strict-delete ops when the requested key is absent.
/// </summary>
public class EntryNotFoundException : GeodeException
{
    public EntryNotFoundException() { }

    public EntryNotFoundException(string message)
        : base(message) { }

    public EntryNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
