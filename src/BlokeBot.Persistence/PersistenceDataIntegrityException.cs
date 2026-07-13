namespace BlokeBot.Persistence;

public sealed class PersistenceDataIntegrityException : Exception
{
    internal PersistenceDataIntegrityException(Type discriminatorType)
        : base($"Persisted {discriminatorType.Name} data is invalid.")
    {
        DiscriminatorType = discriminatorType;
    }

    public Type DiscriminatorType { get; }
}
