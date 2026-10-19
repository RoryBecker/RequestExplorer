using System.Globalization;
using System.Text.RegularExpressions;

namespace RequestExplorer.Services.Variables;

/// <summary>
/// Resolves special variable <em>values</em> (recipes) at read/substitute time.
/// Stored value stays the recipe; scope/tooltips/send see the computed result.
/// </summary>
/// <remarks>
/// <para>Date-only (UTC by default):</para>
/// <list type="bullet">
/// <item><c>@date(today)</c> → UTC calendar date, <c>yyyy-MM-dd</c></item>
/// <item><c>@date(today-7d)</c> / <c>@date(today+3d)</c> → offset in whole days</item>
/// <item><c>@date(today-7d, yyyyMMdd)</c> → custom .NET format</item>
/// <item><c>@date(today, yyyy-MM-dd, local)</c> → local calendar date</item>
/// </list>
/// <para>Date-time (UTC by default):</para>
/// <list type="bullet">
/// <item><c>@date(now)</c> → UTC instant, <c>yyyy-MM-ddTHH:mm:ssZ</c></item>
/// <item><c>@date(now-7d)</c> → same instant shifted by whole days</item>
/// <item><c>@date(now, local)</c> → local clock time (no <c>Z</c> suffix by default)</item>
/// </list>
/// </remarks>
public static partial class VariableValueRecipes
{
    private const string DefaultTodayFormat = "yyyy-MM-dd";
    private const string DefaultNowUtcFormat = "yyyy-MM-ddTHH:mm:ss'Z'";
    private const string DefaultNowLocalFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>
    /// If <paramref name="storedValue"/> is a known recipe, returns the computed string;
    /// otherwise returns <paramref name="storedValue"/> unchanged.
    /// </summary>
    public static string Resolve(string? storedValue, DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return storedValue ?? string.Empty;
        }

        var trimmed = storedValue.Trim();
        if (TryResolveDate(trimmed, utcNow ?? DateTimeOffset.UtcNow, out var resolved))
        {
            return resolved;
        }

        return storedValue;
    }

    /// <summary>True when the stored value is a date/now recipe (whether or not it evaluates).</summary>
    public static bool IsDateRecipe(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return false;
        }

        return DateRecipeRegex().IsMatch(storedValue.Trim());
    }

    private static bool TryResolveDate(string trimmed, DateTimeOffset utcNow, out string resolved)
    {
        resolved = string.Empty;
        var match = DateRecipeRegex().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var anchor = match.Groups[1].Value;
        var isToday = anchor.Equals("today", StringComparison.OrdinalIgnoreCase);
        var isNow = anchor.Equals("now", StringComparison.OrdinalIgnoreCase);
        if (!isToday && !isNow)
        {
            return false;
        }

        var dayOffset = 0;
        if (match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
        {
            var offsetText = match.Groups[2].Value.Replace(" ", "", StringComparison.Ordinal);
            if (!int.TryParse(offsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out dayOffset))
            {
                return false;
            }
        }

        var third = match.Groups[3].Success ? match.Groups[3].Value.Trim() : string.Empty;
        var fourth = match.Groups[4].Success ? match.Groups[4].Value.Trim() : string.Empty;

        bool useLocal;
        string format;
        if (fourth.Length > 0)
        {
            useLocal = fourth.Equals("local", StringComparison.OrdinalIgnoreCase);
            format = third.Length > 0 ? third : DefaultFormat(isNow, useLocal);
        }
        else if (IsTimeZoneToken(third))
        {
            useLocal = third.Equals("local", StringComparison.OrdinalIgnoreCase);
            format = DefaultFormat(isNow, useLocal);
        }
        else
        {
            useLocal = false;
            format = third.Length > 0 ? third : DefaultFormat(isNow, useLocal: false);
        }

        try
        {
            DateTime value;
            if (isToday)
            {
                value = useLocal
                    ? DateTime.Now.Date.AddDays(dayOffset)
                    : utcNow.UtcDateTime.Date.AddDays(dayOffset);
            }
            else
            {
                value = useLocal
                    ? DateTime.Now.AddDays(dayOffset)
                    : DateTime.SpecifyKind(utcNow.UtcDateTime.AddDays(dayOffset), DateTimeKind.Utc);
            }

            resolved = value.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string DefaultFormat(bool isNow, bool useLocal) =>
        isNow
            ? (useLocal ? DefaultNowLocalFormat : DefaultNowUtcFormat)
            : DefaultTodayFormat;

    private static bool IsTimeZoneToken(string value) =>
        value.Equals("UTC", StringComparison.OrdinalIgnoreCase)
        || value.Equals("local", StringComparison.OrdinalIgnoreCase);

    // @date( today|now [±N d] [, format] [, UTC|local] )
    [GeneratedRegex(
        """
        ^@date\(\s*(today|now)\s*(?:([+-]\s*\d+)\s*d)?\s*(?:,\s*([^,)]+?))?\s*(?:,\s*(UTC|local))?\s*\)$
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex DateRecipeRegex();
}
