using RentRoad.Models.Enums;
using RentRoad.Services.Rentals;
using RentRoad.ViewModels.Listings;

namespace RentRoad.ViewModels.Rentals;

/// <summary>Displays stored financial terms and inclusive rental dates without exposing database entities.</summary>
public sealed class RentalManagementItemViewModel
{
    public int RentalRequestId { get; }
    public int ListingId { get; }
    public int? ConversationId { get; }
    /// <summary>Controls public links without granting access to a nonpublic listing or its images.</summary>
    public bool IsListingPublic { get; }
    public string ListingTitle { get; }
    public RentalHistoryImage? PrimaryImage { get; }
    public string CounterpartyDisplayName { get; }
    public ListingPriceViewModel DailyPrice { get; }
    public ListingPriceViewModel TotalPrice { get; }
    public DateOnly FirstDay { get; }
    public DateOnly LastDay { get; }
    public int RentalDays { get; }
    public TimeOnly PickupTime { get; }
    public TimeOnly ReturnTime { get; }
    public RentalStatus Status { get; }
    public string StatusText => SharedResource.Get($"RentalStatus.{Status}");
    public DateTime CreatedAtUtc { get; }
    public DateTime? DecisionAtUtc { get; }
    public DateTime? CompletedAtUtc { get; }
    public RentalAvailableActions Actions { get; }
    public string DurationText => SharedResource.Get(RentalDays == 1 ? "RentalRequest.DurationOne" : "RentalRequest.DurationMany", RentalDays);

    /// <summary>Maps only safe read fields, retaining original cents and subtracting one calendar day from the exclusive boundary.</summary>
    public RentalManagementItemViewModel(RentalHistoryItem item)
    {
        RentalRequestId = item.RentalRequestId;
        ListingId = item.ListingId;
        ConversationId = item.ConversationId;
        IsListingPublic = item.IsListingPublic;
        ListingTitle = string.IsNullOrWhiteSpace(item.ListingTitle) ? SharedResource.Get("MyListings.Untitled") : item.ListingTitle;
        PrimaryImage = item.PrimaryImage;
        CounterpartyDisplayName = item.CounterpartyDisplayName;
        DailyPrice = new(item.DailyPriceAtRequestCents);
        TotalPrice = new(item.TotalPriceCents);
        FirstDay = item.StartDate;
        LastDay = item.EndDate.AddDays(-1);
        RentalDays = item.EndDate.DayNumber - item.StartDate.DayNumber;
        PickupTime = item.PickupTime;
        ReturnTime = item.ReturnTime;
        Status = item.Status;
        CreatedAtUtc = item.CreatedAtUtc;
        DecisionAtUtc = item.DecisionAtUtc;
        CompletedAtUtc = item.CompletedAtUtc;
        Actions = item.Actions;
    }
}

/// <summary>Prepares either participant history view with bounded pagination and localized page context.</summary>
public sealed class RentalManagementViewModel
{
    public bool IsIncoming { get; }
    public bool HasLoadedHistory { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<RentalManagementItemViewModel> Rentals { get; }
    public int TotalCount { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalPages { get; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public string Heading => SharedResource.Get(IsIncoming ? "RentalManagement.Incoming" : "RentalManagement.Mine");
    public string EmptyMessage => SharedResource.Get(IsIncoming ? "RentalManagement.IncomingEmpty" : "RentalManagement.MineEmpty");

    /// <summary>Maps a scoped page or explicit read failure without querying or inferring ownership in MVC.</summary>
    public RentalManagementViewModel(RentalHistoryPage? page, bool incoming, IReadOnlyList<string>? errors = null)
    {
        IsIncoming = incoming;
        HasLoadedHistory = page is not null;
        Errors = errors ?? Array.Empty<string>();
        Rentals = page?.Items.Select(item => new RentalManagementItemViewModel(item)).ToArray() ?? Array.Empty<RentalManagementItemViewModel>();
        TotalCount = page?.TotalCount ?? 0;
        PageNumber = page?.PageNumber ?? 1;
        PageSize = page?.PageSize ?? RentalHistoryParameters.DEFAULT_PAGE_SIZE;
        TotalPages = page?.TotalPages ?? 0;
    }
}

/// <summary>Supplies one server-selected confirmation action with navigation context, excluding participant and financial inputs.</summary>
public sealed record RentalManagementActionViewModel(int RentalRequestId, string Action, int PageNumber, int PageSize);
