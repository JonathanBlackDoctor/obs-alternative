using System.Text.RegularExpressions;

namespace SilentStream.Core.YouTube;

/// <summary>
/// Expands the broadcast title template (plan §3.7): every "{...}" group is treated as a
/// .NET date format string, e.g. "라이브 - {yyyy-MM-dd HH:mm}" → "라이브 - 2026-06-12 09:30".
/// </summary>
public static partial class TitleTemplater
{
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TokenRegex();

    public static string Expand(string template, DateTime timestamp) =>
        TokenRegex().Replace(template, m =>
        {
            try
            {
                return timestamp.ToString(m.Groups[1].Value);
            }
            catch (FormatException)
            {
                return m.Value; // leave unknown tokens untouched
            }
        });

    /// <summary>
    /// Period-aware overload (확장계획서 §5, D6): the {교시} token (or {교시:00} with a numeric
    /// format) is replaced with <paramref name="periodNumber"/>; every other "{...}" group is a
    /// date format applied to <paramref name="timestamp"/>.
    /// e.g. "{교시}교시 - {yyyy-MM-dd}" + (2026-06-14, 1) → "1교시 - 2026-06-14".
    /// </summary>
    public static string Expand(string template, DateTime timestamp, int periodNumber) =>
        TokenRegex().Replace(template, m =>
        {
            var token = m.Groups[1].Value;
            if (token == "교시")
            {
                return periodNumber.ToString();
            }
            if (token.StartsWith("교시:", StringComparison.Ordinal))
            {
                try
                {
                    return periodNumber.ToString(token["교시:".Length..]);
                }
                catch (FormatException)
                {
                    return m.Value;
                }
            }
            try
            {
                return timestamp.ToString(token);
            }
            catch (FormatException)
            {
                return m.Value; // leave unknown tokens untouched
            }
        });
}
