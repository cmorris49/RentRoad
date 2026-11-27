using System.Text.RegularExpressions;

namespace RentRoad.Services.Reviews;

/// <summary>
/// Minimizes obvious contact information and rejects unsafe generated presentation content.
/// </summary>
internal static class ReviewSummaryText
{
    private static readonly Regex contact = new(
        @"(?i)\b[\w.+-]+@[\w.-]+\.[a-z]{2,}\b|(?:https?://|www\.)\S+|(?<!\w)@\w+|(?<!\w)\+?\d[\d\s().-]{6,}\d(?!\w)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex whitespace = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Removes common contact patterns before limiting public comment length.
    /// </summary>
    /// <param name="comment">The original public comment.</param>
    /// <param name="limit">The maximum number of transmitted characters.</param>
    /// <returns>The minimized comment or null when it has no meaningful text.</returns>
    internal static string? Minimize(string? comment, int limit)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var text = whitespace.Replace(contact.Replace(comment, " "), " ").Trim();
        text = text[..Math.Min(text.Length, limit)];
        return text.Count(char.IsLetter) >= 3 ? text : null;
    }

    /// <summary>
    /// Accepts bounded plain text without markup, contact details, code, or model-controlled URLs.
    /// </summary>
    /// <param name="text">The generated text to validate.</param>
    /// <param name="limit">The maximum permitted length.</param>
    /// <returns>Normalized plain text, or null when validation fails.</returns>
    internal static string? Validate(string? text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > limit
            || text.Any(character => character is '<' or '>' or '`' || char.IsControl(character))
            || contact.IsMatch(text))
        {
            return null;
        }

        var normalized = whitespace.Replace(text, " ").Trim();
        return normalized.Count(char.IsLetter) >= 10 ? normalized : null;
    }
}
