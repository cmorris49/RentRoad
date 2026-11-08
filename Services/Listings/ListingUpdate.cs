using RentRoad.Models.Enums;

namespace RentRoad.Services.Listings;

/// <summary>
/// Contains only the listing details an owner may edit.
/// </summary>
/// <remarks>
/// Scalar fields replace the existing editable details; null values represent incomplete draft fields.
/// A null availability collection leaves stored windows unchanged; an empty collection removes them.
/// Ownership, status, timestamps, image metadata, and rental records cannot be submitted through this contract.
/// </remarks>
public sealed record ListingUpdate
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public ListingCategory? Category { get; init; }

    public ItemCondition? Condition { get; init; }

    public long? DailyPriceCents { get; init; }

    public string? PickupArea { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public IReadOnlyList<ListingAvailabilityPeriod>? AvailabilityWindows { get; init; }
}

/// <summary>
/// Represents an availability window with an inclusive start and an exclusive end.
/// </summary>
public sealed record ListingAvailabilityPeriod(DateOnly StartDate, DateOnly EndDate);
