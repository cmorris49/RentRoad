namespace RentRoad.Services.Listings;

/// <summary>Shares the existing half-open window coverage rule between listing changes and rental request validation.</summary>
internal static class AvailabilityCoverage
{
    /// <summary>Checks complete coverage by the union of adjacent or overlapping windows without requiring a single enclosing window.</summary>
    internal static bool CoversPeriod(IReadOnlyList<ListingAvailabilityPeriod> windows, ListingAvailabilityPeriod requested)
    {
        var coveredThrough = requested.StartDate;
        foreach (var window in windows.OrderBy(window => window.StartDate).ThenBy(window => window.EndDate))
        {
            if (window.EndDate <= coveredThrough)
            {
                continue;
            }
            if (window.StartDate > coveredThrough)
            {
                return false;
            }
            coveredThrough = window.EndDate;
            if (coveredThrough >= requested.EndDate)
            {
                return true;
            }
        }
        return false;
    }
}
