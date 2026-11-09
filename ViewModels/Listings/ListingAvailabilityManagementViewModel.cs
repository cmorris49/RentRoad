using System.ComponentModel.DataAnnotations;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Binds only a half-open DateOnly range, leaving date rules and duplicate/conflict checks to the service.
/// </summary>
public class ListingAvailabilityWindowInputViewModel
{
    [Required]
    [DataType(DataType.Date)]
    public DateOnly? StartDate { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Maps an inclusive start and exclusive end without adjusting dates or accepting relationship identifiers.
    /// </summary>
    public ListingAvailabilityPeriod ToServicePeriod() => new(StartDate.GetValueOrDefault(), EndDate.GetValueOrDefault());
}

/// <summary>
/// Adds an optional private reason to the same date input contract; model length validation remains in the service.
/// </summary>
public class ListingAvailabilityBlockInputViewModel : ListingAvailabilityWindowInputViewModel
{
    public string? Reason { get; set; }

    /// <summary>
    /// Maps editable block dates and reason without accepting owner, listing, or block IDs.
    /// </summary>
    public ListingAvailabilityBlockUpdate ToServiceBlock() => new(StartDate.GetValueOrDefault(), EndDate.GetValueOrDefault(), Reason);
}

/// <summary>
/// Represents a complete window replacement; an absent or empty window list explicitly requests clearing the set.
/// </summary>
public sealed class ListingAvailabilityReplacementViewModel
{
    public List<ListingAvailabilityWindowInputViewModel>? Windows { get; set; }

    /// <summary>
    /// Maps replacement dates, preserving empty-set intent and leaving all listing scalars and blocks outside the contract.
    /// </summary>
    public IReadOnlyList<ListingAvailabilityPeriod> ToServicePeriods() => Windows?
        .Select(window => window?.ToServicePeriod() ?? new ListingAvailabilityPeriod(default, default)).ToArray()
        ?? Array.Empty<ListingAvailabilityPeriod>();
}

/// <summary>
/// Identifies the submitted availability form for targeted validation feedback and future per-record controls.
/// </summary>
public enum ListingAvailabilityOperation
{
    CreateWindow,
    UpdateWindow,
    RemoveWindow,
    ReplaceWindows,
    CreateBlock,
    UpdateBlock,
    RemoveBlock
}

/// <summary>
/// Preserves the service outcome and source errors without converting conflicts into successful updates.
/// </summary>
public sealed record ListingAvailabilityFeedbackViewModel(ListingAvailabilityOperation Operation, int? RecordId,
    ListingServiceStatus Status, IReadOnlyList<ListingValidationError> Errors);

/// <summary>
/// Associates an editable window ID with the existing public half-open date display component.
/// </summary>
public sealed record OwnerAvailabilityWindowViewModel(int WindowId, ListingAvailabilityViewModel Period);

/// <summary>
/// Contains owner-only block details; private reasons are never added to the public availability ViewModel.
/// </summary>
public sealed record OwnerAvailabilityBlockViewModel(int BlockId, ListingAvailabilityViewModel Period, string? Reason);

/// <summary>
/// Supplies trusted availability display data and flat form values for a future owner-only management page.
/// </summary>
public sealed class ListingAvailabilityManagementViewModel : ListingAvailabilityBlockInputViewModel
{
    public int ListingId { get; }
    public ListingStatus Status { get; }
    public bool CanEdit => Status != ListingStatus.Archived;
    public IReadOnlyList<OwnerAvailabilityWindowViewModel> AvailabilityWindows { get; }
    public IReadOnlyList<OwnerAvailabilityBlockViewModel> AvailabilityBlocks { get; }
    public IReadOnlyList<ListingAvailabilityWindowInputViewModel> Windows { get; }
    public ListingAvailabilityFeedbackViewModel? Feedback { get; }

    /// <summary>
    /// Maps owner-authorized data and preserves submitted dates/reasons/replacement rows without exposing other listing fields.
    /// </summary>
    public ListingAvailabilityManagementViewModel(Listing listing, ListingAvailabilityWindowInputViewModel? input = null,
        ListingAvailabilityReplacementViewModel? replacement = null, ListingAvailabilityFeedbackViewModel? feedback = null)
    {
        ListingId = listing.ListingId;
        Status = listing.Status;
        AvailabilityWindows = listing.AvailabilityWindows.Select(window => new OwnerAvailabilityWindowViewModel(window.AvailabilityWindowId,
            new ListingAvailabilityViewModel(window.StartDate, window.EndDate))).ToArray();
        AvailabilityBlocks = listing.AvailabilityBlocks.Select(block => new OwnerAvailabilityBlockViewModel(block.AvailabilityBlockId,
            new ListingAvailabilityViewModel(block.StartDate, block.EndDate), block.Reason)).ToArray();
        StartDate = input?.StartDate;
        EndDate = input?.EndDate;
        Reason = (input as ListingAvailabilityBlockInputViewModel)?.Reason;
        Windows = replacement is null ? listing.AvailabilityWindows.Select(window => new ListingAvailabilityWindowInputViewModel
            { StartDate = window.StartDate, EndDate = window.EndDate }).ToArray()
            : replacement.Windows?.Select(window => new ListingAvailabilityWindowInputViewModel
                { StartDate = window?.StartDate, EndDate = window?.EndDate }).ToArray()
                ?? Array.Empty<ListingAvailabilityWindowInputViewModel>();
        Feedback = feedback;
    }
}
