namespace RentRoad.ViewModels.Account;

/// <summary>Contains only the authentication state and safe account label needed by shared navigation.</summary>
public sealed record AccountNavigationViewModel(bool IsSignedIn, string DisplayName);
