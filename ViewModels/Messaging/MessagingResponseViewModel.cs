using RentRoad.Services.Messaging;

namespace RentRoad.ViewModels.Messaging;

/// <summary>Preserves a stable service resource key and field while exposing only presentation-resolved safe wording.</summary>
public sealed record MessagingErrorViewModel(string Field, string ResourceKey, string Message)
{
    /// <summary>Resolves one service error using the existing shared resource catalogue and formatting arguments.</summary>
    public static MessagingErrorViewModel FromResult(MessagingError error) => new(error.Field, error.ResourceKey,
        SharedResource.Get(error.ResourceKey, error.Arguments));
}

/// <summary>Provides one small JSON envelope with stable status names, replay flags and safe presentation data.</summary>
public sealed record MessagingResponseViewModel<T>(bool IsSuccess, string Status, T? Data, bool AlreadyExists, bool AlreadyApplied,
    IReadOnlyList<MessagingErrorViewModel> Errors)
{
    /// <summary>Gets the live dispatch outcome separately from successful SQL persistence, when a send was confirmed.</summary>
    public string? NotificationStatus { get; init; }

    /// <summary>Maps only confirmed service data and resolves field errors at the MVC boundary.</summary>
    public static MessagingResponseViewModel<T> FromResult<TSource>(MessagingResult<TSource> result, Func<TSource, T> map,
        MessageRealtimeNotificationStatus? notificationStatus = null) => new(result.IsSuccess, result.Status.ToString(),
        result.IsSuccess && result.Value is not null ? map(result.Value) : default, result.AlreadyExists, result.AlreadyApplied,
        result.Errors.Select(MessagingErrorViewModel.FromResult).ToArray()) { NotificationStatus = notificationStatus?.ToString() };
}
