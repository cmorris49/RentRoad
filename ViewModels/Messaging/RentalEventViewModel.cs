using RentRoad.Models.Enums;
using RentRoad.Services.Messaging;
using RentRoad.ViewModels.Listings;

namespace RentRoad.ViewModels.Messaging;

/// <summary>
/// Provides plain localized automatic-message text and a participant-specific rental link.
/// </summary>
public sealed class RentalEventViewModel
{
    public string Heading => SharedResource.Get("RentalEvent.AutomaticSender");
    public string ListingTitle { get; }
    public string Summary { get; }
    public string StatusLabel => SharedResource.Get("RentalEvent.CurrentStatus");
    public string StatusText { get; }
    public IReadOnlyList<RentalEventDetail> Details { get; }
    public string Body => string.Join("\n", Summary,
        $"{Details[0].Label}: {Details[0].Value} · {Details[1].Label}: {Details[1].Value}",
        $"{Details[2].Value} · {Details[3].Label}: {Details[3].Value}",
        $"{StatusLabel}: {StatusText}");
    public string ActionLabel { get; }
    public string ActionPath { get; }

    /// <summary>
    /// Formats verified rental actions and terms without attributing the message to either participant.
    /// </summary>
    /// <param name="message">The authorized trusted event projection.</param>
    public RentalEventViewModel(RentalRequestMessage message)
    {
        ListingTitle = string.IsNullOrWhiteSpace(message.ListingTitle)
            ? SharedResource.Get("MyListings.Untitled") : message.ListingTitle;
        var approved = message.EventType == RentalMessageEventType.RequestApproved;
        var summaryKey = approved
            ? message.IsOwner ? "RentalEvent.OwnerApproved" : "RentalEvent.RenterApproved"
            : message.IsOwner ? "RentalEvent.OwnerSummary"
                : message.Status == RentalStatus.Pending ? "RentalEvent.RenterPending" : "RentalEvent.RenterReceived";

        Summary = SharedResource.Get(summaryKey, ListingTitle);
        StatusText = SharedResource.Get($"RentalStatus.{message.Status}");
        var rentalDays = message.EndDate.DayNumber - message.StartDate.DayNumber;
        Details = new RentalEventDetail[]
        {
            new(SharedResource.Get("Availability.FirstDay"), SharedResource.Get("Availability.SingleDate", message.StartDate)),
            new(SharedResource.Get("Availability.LastDay"), SharedResource.Get("Availability.SingleDate", message.EndDate.AddDays(-1))),
            new(SharedResource.Get("RentalRequest.Duration"), SharedResource.Get(rentalDays == 1 ? "RentalRequest.DurationOne" : "RentalRequest.DurationMany", rentalDays)),
            new(SharedResource.Get(approved ? "RentalEvent.ApprovedTotal" : "RentalEvent.RequestedTotal"), new ListingPriceViewModel(message.TotalPriceCents).FormattedAmount)
        };
        ActionLabel = SharedResource.Get(message.IsOwner ? "RentalEvent.ReviewRequest" : "RentalEvent.ViewMyRental");
        ActionPath = $"rentals/{(message.IsOwner ? "incoming" : "mine")}?requestId={message.RentalRequestId}#rental-{message.RentalRequestId}";
    }
}

/// <summary>Pairs a localized accessible label with one formatted rental term.</summary>
public sealed record RentalEventDetail(string Label, string Value);
