using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomerFeedback.Shared.Security;

/// <summary>Normalises untrusted text: strips markup and control characters, collapses whitespace.</summary>
public static partial class InputSanitizer
{
    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex RepeatedSpaceRegex();

    public static string? Clean(string? input, bool allowNewLines = false)
    {
        if (input is null)
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(input).Normalize(NormalizationForm.FormC);
        var withoutTags = HtmlTagRegex().Replace(decoded, string.Empty).Replace("<", string.Empty).Replace(">", string.Empty);

        var sb = new StringBuilder(withoutTags.Length);
        foreach (var ch in withoutTags)
        {
            if (ch is '\n' or '\r')
            {
                sb.Append(allowNewLines ? '\n' : ' ');
            }
            else if (!char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        var cleaned = RepeatedSpaceRegex().Replace(sb.ToString(), " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static string CleanRequired(string? input) => Clean(input) ?? string.Empty;

    public static string? CleanEmail(string? input) => Clean(input)?.ToLowerInvariant();
}
