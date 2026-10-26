namespace ConveyancingSearch.Service.Solicitors;

public sealed class SourceOptions
{
    public int ParallelDownloads { get; set; } = 4;
    public int MaxPages { get; set; } = 512;
    public int RequestTimeoutSeconds { get; set; } = 20;
}

