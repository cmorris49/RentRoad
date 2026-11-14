using Microsoft.AspNetCore.Identity;

namespace RentRoad.Localization;

/// <summary>
/// Localizes standard Identity error descriptions while retaining their framework error codes and validation behavior.
/// </summary>
public sealed class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    /// <summary>
    /// Provides the resource-backed DefaultError description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError DefaultError() =>
        new()
        {
            Code = nameof(DefaultError),
            Description = SharedResource.Get("Identity.DefaultError")
        };

    /// <summary>
    /// Provides the resource-backed ConcurrencyFailure description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError ConcurrencyFailure() =>
        new()
        {
            Code = nameof(ConcurrencyFailure),
            Description = SharedResource.Get("Identity.ConcurrencyFailure")
        };

    /// <summary>
    /// Provides the resource-backed PasswordMismatch description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError PasswordMismatch() =>
        new()
        {
            Code = nameof(PasswordMismatch),
            Description = SharedResource.Get("Identity.PasswordMismatch")
        };

    /// <summary>
    /// Provides the resource-backed InvalidToken description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError InvalidToken() =>
        new()
        {
            Code = nameof(InvalidToken),
            Description = SharedResource.Get("Identity.InvalidToken")
        };

    /// <summary>
    /// Provides the resource-backed RecoveryCodeRedemptionFailed description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError RecoveryCodeRedemptionFailed() =>
        new()
        {
            Code = nameof(RecoveryCodeRedemptionFailed),
            Description = SharedResource.Get("Identity.RecoveryCodeRedemptionFailed")
        };

    /// <summary>
    /// Provides the resource-backed LoginAlreadyAssociated description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError LoginAlreadyAssociated() =>
        new()
        {
            Code = nameof(LoginAlreadyAssociated),
            Description = SharedResource.Get("Identity.LoginAlreadyAssociated")
        };

    /// <summary>
    /// Provides the resource-backed InvalidUserName description with its original Identity code.
    /// </summary>
    /// <param name="userName">The invalid user name, or null when no name was supplied.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError InvalidUserName(string? userName) =>
        new()
        {
            Code = nameof(InvalidUserName),
            Description = SharedResource.Get("Identity.InvalidUserName", userName ?? string.Empty)
        };

    /// <summary>
    /// Provides the resource-backed InvalidEmail description with its original Identity code.
    /// </summary>
    /// <param name="email">The invalid email address, or null when no address was supplied.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError InvalidEmail(string? email) =>
        new()
        {
            Code = nameof(InvalidEmail),
            Description = SharedResource.Get("Identity.InvalidEmail", email ?? string.Empty)
        };

    /// <summary>
    /// Provides the resource-backed DuplicateUserName description with its original Identity code.
    /// </summary>
    /// <param name="userName">The user name that is already in use.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError DuplicateUserName(string userName) =>
        new()
        {
            Code = nameof(DuplicateUserName),
            Description = SharedResource.Get("Identity.DuplicateUserName", userName)
        };

    /// <summary>
    /// Provides the resource-backed DuplicateEmail description with its original Identity code.
    /// </summary>
    /// <param name="email">The email address that is already in use.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError DuplicateEmail(string email) =>
        new()
        {
            Code = nameof(DuplicateEmail),
            Description = SharedResource.Get("Identity.DuplicateEmail", email)
        };

    /// <summary>
    /// Provides the resource-backed InvalidRoleName description with its original Identity code.
    /// </summary>
    /// <param name="role">The invalid role name, or null when no role was supplied.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError InvalidRoleName(string? role) =>
        new()
        {
            Code = nameof(InvalidRoleName),
            Description = SharedResource.Get("Identity.InvalidRoleName", role ?? string.Empty)
        };

    /// <summary>
    /// Provides the resource-backed DuplicateRoleName description with its original Identity code.
    /// </summary>
    /// <param name="role">The role name that is already in use.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError DuplicateRoleName(string role) =>
        new()
        {
            Code = nameof(DuplicateRoleName),
            Description = SharedResource.Get("Identity.DuplicateRoleName", role)
        };

    /// <summary>
    /// Provides the resource-backed UserAlreadyHasPassword description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError UserAlreadyHasPassword() =>
        new()
        {
            Code = nameof(UserAlreadyHasPassword),
            Description = SharedResource.Get("Identity.UserAlreadyHasPassword")
        };

    /// <summary>
    /// Provides the resource-backed UserLockoutNotEnabled description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError UserLockoutNotEnabled() =>
        new()
        {
            Code = nameof(UserLockoutNotEnabled),
            Description = SharedResource.Get("Identity.UserLockoutNotEnabled")
        };

    /// <summary>
    /// Provides the resource-backed UserAlreadyInRole description with its original Identity code.
    /// </summary>
    /// <param name="role">The role to which the user already belongs.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError UserAlreadyInRole(string role) =>
        new()
        {
            Code = nameof(UserAlreadyInRole),
            Description = SharedResource.Get("Identity.UserAlreadyInRole", role)
        };

    /// <summary>
    /// Provides the resource-backed UserNotInRole description with its original Identity code.
    /// </summary>
    /// <param name="role">The role to which the user does not belong.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError UserNotInRole(string role) =>
        new()
        {
            Code = nameof(UserNotInRole),
            Description = SharedResource.Get("Identity.UserNotInRole", role)
        };

    /// <summary>
    /// Provides the resource-backed PasswordTooShort description with its original Identity code.
    /// </summary>
    /// <param name="length">The required minimum password length.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError PasswordTooShort(int length) =>
        new()
        {
            Code = nameof(PasswordTooShort),
            Description = SharedResource.Get("Identity.PasswordTooShort", length)
        };

    /// <summary>
    /// Provides the resource-backed PasswordRequiresUniqueChars description with its original Identity code.
    /// </summary>
    /// <param name="uniqueChars">The required minimum number of distinct password characters.</param>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new()
        {
            Code = nameof(PasswordRequiresUniqueChars),
            Description = SharedResource.Get("Identity.PasswordRequiresUniqueChars", uniqueChars)
        };

    /// <summary>
    /// Provides the resource-backed PasswordRequiresNonAlphanumeric description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new()
        {
            Code = nameof(PasswordRequiresNonAlphanumeric),
            Description = SharedResource.Get("Identity.PasswordRequiresNonAlphanumeric")
        };

    /// <summary>
    /// Provides the resource-backed PasswordRequiresDigit description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError PasswordRequiresDigit() =>
        new()
        {
            Code = nameof(PasswordRequiresDigit),
            Description = SharedResource.Get("Identity.PasswordRequiresDigit")
        };

    /// <summary>
    /// Provides the resource-backed PasswordRequiresLower description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError PasswordRequiresLower() =>
        new()
        {
            Code = nameof(PasswordRequiresLower),
            Description = SharedResource.Get("Identity.PasswordRequiresLower")
        };

    /// <summary>
    /// Provides the resource-backed PasswordRequiresUpper description with its original Identity code.
    /// </summary>
    /// <returns>An Identity error with its original code and the resource-backed description.</returns>
    public override IdentityError PasswordRequiresUpper() =>
        new()
        {
            Code = nameof(PasswordRequiresUpper),
            Description = SharedResource.Get("Identity.PasswordRequiresUpper")
        };
}
