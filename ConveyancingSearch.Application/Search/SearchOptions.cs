namespace ConveyancingSearch.Application.Search;

public sealed class SearchOptions
{
    public int FreshnessHours { get; set; } = 24;
    public int RefreshTimeoutSeconds { get; set; } = 300;
}
