namespace ConveyancingSearch.Application.Contracts;

/// <summary>A database operation failed; the inner exception is for server logs only.</summary>
public sealed class StorageException(Exception innerException)
    : Exception("The database operation could not be completed.", innerException);
