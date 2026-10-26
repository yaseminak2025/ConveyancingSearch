namespace ConveyancingSearch.Application.Contracts;

public enum SearchStatus
{
    /// <summary>Saved results were still fresh; no refresh was attempted.</summary>
    Cached,

    /// <summary>A complete collection was saved during this search.</summary>
    Refreshed,

    /// <summary>The refresh failed; the last saved results are shown with a warning.</summary>
    Fallback,

    /// <summary>The refresh failed and nothing has been saved for this city yet.</summary>
    Unavailable,
}
