namespace ConveyancingSearch.Application.Contracts;

public sealed class SourceException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

