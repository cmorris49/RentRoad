namespace RentRoad.Services.Reviews;

/// <summary>
/// Describes one authenticated renter's completed rental without exposing account or listing storage fields.
/// </summary>
/// <param name="RentalRequestId">The eligible rental identifier.</param>
/// <param name="StartDate">The inclusive start date.</param>
/// <param name="EndDate">The exclusive end date.</param>
public sealed record ReviewRentalChoice(int RentalRequestId, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Provides only the authenticated renter's review editor fields.
/// </summary>
/// <param name="RentalRequestId">The owned Completed rental.</param>
/// <param name="ListingId">The related listing identifier.</param>
/// <param name="Rating">The existing rating, or null for a new review.</param>
/// <param name="Comment">The existing optional comment.</param>
public sealed record ReviewEditorData(int RentalRequestId, int ListingId, int? Rating, string? Comment);
