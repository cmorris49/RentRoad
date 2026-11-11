using System.ComponentModel.DataAnnotations;
using RentRoad.Models;
using RentRoad.Models.Enums;
using RentRoad.Services.Listings;

namespace RentRoad.ViewModels.Listings;

/// <summary>
/// Binds inclusive calendar dates and converts them to the existing half-open service contract.
/// </summary>
public class ListingAvailabilityWindowInputViewModel : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.FirstDayRequired))]
    [Display(Name = nameof(SharedResource.FirstDayLabel), ResourceType = typeof(SharedResource))]
    [DataType(DataType.Date)]
    public DateOnly? StartDate { get; set; }

    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.LastDayRequired))]
    [Display(Name = nameof(SharedResource.LastDayLabel), ResourceType = typeof(SharedResource))]
    [DataType(DataType.Date)]
    public DateOnly? LastDay { get; set; }

    /// <summary>
    /// Maps inclusive form dates to exclusive service boundaries without accepting relationship identifiers.
    /// </summary>
    public ListingAvailabilityPeriod ToServicePeriod() => new(StartDate.GetValueOrDefault(), ExclusiveEndDate());

    /// <summary>Rejects reversed or unrepresentable inclusive dates before any service mutation is attempted.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate == DateOnly.MinValue)
        {
            yield return new ValidationResult(SharedResource.Get("Availability.FirstDayMinimum"), new[] { nameof(StartDate) });
        }
        if (StartDate.HasValue && LastDay.HasValue && LastDay.Value < StartDate.Value)
        {
            yield return new ValidationResult(SharedResource.Get("Availability.LastDayBeforeFirst"), new[] { nameof(LastDay) });
        }
        if (LastDay == DateOnly.MaxValue)
        {
            yield return new ValidationResult(SharedResource.Get("Availability.LastDayMaximum"), new[] { nameof(LastDay) });
        }
    }

    /// <summary>Converts a validated last day using calendar arithmetic, safely retaining invalid input for validation.</summary>
    protected DateOnly ExclusiveEndDate() => LastDay.HasValue && LastDay.Value < DateOnly.MaxValue
        ? LastDay.Value.AddDays(1) : default;
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
    public ListingAvailabilityBlockUpdate ToServiceBlock() => new(StartDate.GetValueOrDefault(), ExclusiveEndDate(), Reason);
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
/// Supplies trusted availability display data and isolates submitted inputs for the owner-only management page.
/// </summary>
public sealed class ListingAvailabilityManagementViewModel : ListingAvailabilityBlockInputViewModel
{
    public int ListingId { get; }
    public string? ListingTitle { get; }
    public ListingStatus Status { get; }
    public bool CanEdit => Status != ListingStatus.Archived;
    public IReadOnlyList<OwnerAvailabilityWindowViewModel> AvailabilityWindows { get; }
    public IReadOnlyList<OwnerAvailabilityBlockViewModel> AvailabilityBlocks { get; }
    public IReadOnlyList<ListingAvailabilityWindowInputViewModel> Windows { get; }
    public ListingAvailabilityFeedbackViewModel? Feedback { get; }

    /// <summary>
    /// Identifies the submitted operation so only its form receives attempted values and validation messages.
    /// </summary>
    public bool IsSubmitted(ListingAvailabilityOperation operation, int? recordId = null) =>
        Feedback?.Operation == operation && Feedback.RecordId == recordId;

    /// <summary>
    /// Supplies a new or saved window form, preserving attempted dates only for the matching failed submission.
    /// </summary>
    public ListingAvailabilityFormViewModel WindowForm(OwnerAvailabilityWindowViewModel? window = null)
    {
        var submitted = IsSubmitted(window is null ? ListingAvailabilityOperation.CreateWindow : ListingAvailabilityOperation.UpdateWindow, window?.WindowId);
        var input = submitted ? this : new ListingAvailabilityWindowInputViewModel { StartDate = window?.Period.StartDate, LastDay = window?.Period.LastIncludedDate };
        return new(ListingId, false, window?.WindowId, submitted, input);
    }

    /// <summary>
    /// Supplies a new or saved private block form without copying another form's rejected dates or reason.
    /// </summary>
    public ListingAvailabilityFormViewModel BlockForm(OwnerAvailabilityBlockViewModel? block = null)
    {
        var submitted = IsSubmitted(block is null ? ListingAvailabilityOperation.CreateBlock : ListingAvailabilityOperation.UpdateBlock, block?.BlockId);
        var input = submitted ? this : new ListingAvailabilityBlockInputViewModel { StartDate = block?.Period.StartDate, LastDay = block?.Period.LastIncludedDate, Reason = block?.Reason };
        return new(ListingId, true, block?.BlockId, submitted, input);
    }

    /// <summary>
    /// Maps owner-authorized data and preserves submitted dates/reasons/replacement rows without exposing other listing fields.
    /// </summary>
    public ListingAvailabilityManagementViewModel(Listing listing, ListingAvailabilityWindowInputViewModel? input = null,
        ListingAvailabilityReplacementViewModel? replacement = null, ListingAvailabilityFeedbackViewModel? feedback = null)
    {
        ListingId = listing.ListingId;
        ListingTitle = listing.Title;
        Status = listing.Status;
        AvailabilityWindows = listing.AvailabilityWindows.OrderBy(window => window.StartDate).ThenBy(window => window.EndDate).ThenBy(window => window.AvailabilityWindowId)
            .Select(window => new OwnerAvailabilityWindowViewModel(window.AvailabilityWindowId,
            new ListingAvailabilityViewModel(window.StartDate, window.EndDate))).ToArray();
        AvailabilityBlocks = listing.AvailabilityBlocks.OrderBy(block => block.StartDate).ThenBy(block => block.EndDate).ThenBy(block => block.AvailabilityBlockId)
            .Select(block => new OwnerAvailabilityBlockViewModel(block.AvailabilityBlockId,
            new ListingAvailabilityViewModel(block.StartDate, block.EndDate), block.Reason)).ToArray();
        StartDate = input?.StartDate;
        LastDay = input?.LastDay;
        Reason = (input as ListingAvailabilityBlockInputViewModel)?.Reason;
        Windows = replacement is null ? listing.AvailabilityWindows.Select(window => new ListingAvailabilityWindowInputViewModel
            { StartDate = window.StartDate, LastDay = window.EndDate.AddDays(-1) }).ToArray()
            : replacement.Windows?.Select(window => new ListingAvailabilityWindowInputViewModel
                { StartDate = window?.StartDate, LastDay = window?.LastDay }).ToArray()
                ?? Array.Empty<ListingAvailabilityWindowInputViewModel>();
        Feedback = feedback;
    }
}
