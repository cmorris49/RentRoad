namespace RentRoad.Services.Reviews;

/// <summary>
/// Configures explicitly selected Gemini models and bounded summary generation costs.
/// </summary>
public sealed class GeminiReviewSummaryOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MinimumComments { get; set; } = 2;
    public int MaxReviews { get; set; } = 40;
    public int MaxCommentCharacters { get; set; } = 600;
    public int MaxInputCharacters { get; set; } = 12000;
    public int MaxSummaryCharacters { get; set; } = 900;
    public int MaxOutputTokens { get; set; } = 1024;
    public int CooldownSeconds { get; set; } = 300;
    public int MaxGenerationsPerDay { get; set; } = 3;
    public int MaxConcurrentGenerations { get; set; } = 2;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Length <= 512
        && !ApiKey.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
        && Model.Length is > 0 and <= 120
        && Model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
        && TimeoutSeconds is >= 5 and <= 120
        && MinimumComments is >= 2 and <= 20
        && MaxReviews >= MinimumComments && MaxReviews <= 100
        && MaxCommentCharacters is >= 40 and <= 2000
        && MaxInputCharacters is >= 100 and <= 30000
        && MaxSummaryCharacters is >= 100 and <= 1000
        && MaxOutputTokens is >= 128 and <= 4096
        && (CooldownSeconds == 0 || CooldownSeconds is >= 60 and <= 86400)
        && MaxGenerationsPerDay is >= 1 and <= 30
        && MaxConcurrentGenerations is >= 1 and <= 4;
}
