using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using RentRoad.Services.Rentals;

namespace RentRoad.ViewModels.Rentals;

/// <summary>Accepts only inclusive rental dates and optional informational times, excluding identity, status and financial terms.</summary>
[Bind("FirstDay,LastDay,PickupTime,ReturnTime")]
public class RentalRequestInputViewModel : IValidatableObject
{
    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.FirstDayRequired))]
    [Display(Name = nameof(SharedResource.FirstDayLabel), ResourceType = typeof(SharedResource))]
    [DataType(DataType.Date)]
    public DateOnly? FirstDay { get; set; }

    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = nameof(SharedResource.LastDayRequired))]
    [Display(Name = nameof(SharedResource.LastDayLabel), ResourceType = typeof(SharedResource))]
    [DataType(DataType.Date)]
    public DateOnly? LastDay { get; set; }

    [DataType(DataType.Time)]
    [Display(Name = nameof(SharedResource.RentalPickupTimeLabel), ResourceType = typeof(SharedResource))]
    public TimeOnly? PickupTime { get; set; }

    [DataType(DataType.Time)]
    [Display(Name = nameof(SharedResource.RentalReturnTimeLabel), ResourceType = typeof(SharedResource))]
    public TimeOnly? ReturnTime { get; set; }

    [Display(Name = nameof(SharedResource.RentalDurationLabel), ResourceType = typeof(SharedResource))]
    public int? RentalDays => FirstDay.HasValue && LastDay.HasValue && FirstDay != DateOnly.MinValue
        && LastDay < DateOnly.MaxValue && FirstDay <= LastDay ? LastDay.Value.DayNumber - FirstDay.Value.DayNumber + 1 : null;

    /// <summary>Initializes an empty request without selecting dates or creating rental data.</summary>
    public RentalRequestInputViewModel() { }

    /// <summary>Preserves submitted calendar/time values when enriching the form with trusted listing information.</summary>
    protected RentalRequestInputViewModel(RentalRequestInputViewModel input)
    {
        FirstDay = input.FirstDay;
        LastDay = input.LastDay;
        PickupTime = input.PickupTime;
        ReturnTime = input.ReturnTime;
    }

    /// <summary>Rejects invalid or unrepresentable inclusive periods without duplicating future-date or availability business rules.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FirstDay == DateOnly.MinValue)
            yield return new(SharedResource.Get("Availability.FirstDayMinimum"), new[] { nameof(FirstDay) });
        if (FirstDay.HasValue && LastDay.HasValue && LastDay < FirstDay)
            yield return new(SharedResource.Get("Availability.LastDayBeforeFirst"), new[] { nameof(LastDay) });
        if (LastDay == DateOnly.MaxValue)
            yield return new(SharedResource.Get("Availability.LastDayMaximum"), new[] { nameof(LastDay) });
    }

    /// <summary>Converts validated inclusive dates to the existing service contract using the route ID and calendar arithmetic.</summary>
    public RentalRequestCreate ToServiceRequest(int listingId)
    {
        if (!RentalDays.HasValue)
            throw new InvalidOperationException("Validate the inclusive rental dates before mapping a request.");
        return new(listingId, FirstDay!.Value, LastDay!.Value.AddDays(1), PickupTime.GetValueOrDefault(), ReturnTime.GetValueOrDefault());
    }
}
