namespace ConveyancingSearch.Domain;

/// <summary>How a listing relates to the previous successful collection for its city.</summary>
public enum Discovery
{
    New,
    Returned,
    Existing,
}
