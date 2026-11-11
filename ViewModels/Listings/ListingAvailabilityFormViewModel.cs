namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Adds trusted route and validation context to the existing flat date-input contract for a reusable Razor form.
/// </summary>
public sealed class ListingAvailabilityFormViewModel : ListingAvailabilityBlockInputViewModel
{
    public int ListingId { get; }
    public int? RecordId { get; }
    public bool IsBlock { get; }
    public bool IsSubmitted { get; }
    public string FormId => $"{(IsBlock ? "block" : "window")}-{RecordId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "new"}";
    public string ActionName => IsBlock
        ? RecordId.HasValue ? "UpdateBlock" : "CreateBlock"
        : RecordId.HasValue ? "UpdateWindow" : "CreateWindow";

    /// <summary>
    /// Copies editable input into display context; controllers continue binding only the existing narrow input types.
    /// </summary>
    public ListingAvailabilityFormViewModel(int listingId, bool isBlock, int? recordId, bool isSubmitted, ListingAvailabilityWindowInputViewModel input)
    {
        ListingId = listingId;
        IsBlock = isBlock;
        RecordId = recordId;
        IsSubmitted = isSubmitted;
        StartDate = input.StartDate;
        LastDay = input.LastDay;
        Reason = (input as ListingAvailabilityBlockInputViewModel)?.Reason;
    }
}
