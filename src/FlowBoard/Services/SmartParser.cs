using System.Globalization;
using System.Text.RegularExpressions;
using FlowBoard.Models;

namespace FlowBoard.Services;

/// <summary>Result of parsing a quick-add line such as "Edit trailer fri 3pm #video !high".</summary>
public sealed record ParsedTask(string Title, DateTime? Due, Priority? Priority, IReadOnlyList<string> Labels)
{
    public bool HasExtras => Due != null || Priority != null || Labels.Count > 0;

    /// <summary>Short human summary for live previews, e.g. "Due Fri, Jun 12 15:00 · High · #video".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Due is { } d)
                parts.Add("Due " + d.ToString(d.TimeOfDay == TimeSpan.Zero ? "ddd, MMM d" : "ddd, MMM d HH:mm", CultureInfo.CurrentCulture));
            if (Priority is { } p) parts.Add(p.ToString());
            parts.AddRange(Labels.Select(l => "#" + l));
            return string.Join("  ·  ", parts);
        }
    }
}

/// <summary>
/// Natural-language quick add. Understands:
/// dates (today, tomorrow, tonight, mon…sun, next fri, weekend, next week, in 3 days/weeks/months,
/// jun 12, 12 june, 2026-06-12), times (3pm, 3:30pm, 15:30, noon, "at 5"),
/// labels (#name) and priority (!urgent !high !medium !low, !1…!4).
/// </summary>
public static partial class SmartParser
{
    private static readonly string[] MonthNames =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static readonly Dictionary<string, DayOfWeek> Days = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mon"] = DayOfWeek.Monday, ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday, ["tues"] = DayOfWeek.Tuesday, ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday, ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday, ["thur"] = DayOfWeek.Thursday, ["thurs"] = DayOfWeek.Thursday, ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday, ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday, ["saturday"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday, ["sunday"] = DayOfWeek.Sunday,
    };

    public static ParsedTask Parse(string input, DateTime now)
    {
        var text = " " + (input ?? string.Empty).Trim() + " ";
        var labels = new List<string>();
        Priority? priority = null;
        DateTime? date = null;
        TimeSpan? time = null;

        // ---- labels ----
        text = LabelRegex().Replace(text, m =>
        {
            labels.Add(m.Groups[1].Value.Replace('_', ' '));
            return " ";
        });

        // ---- priority ----
        text = PriorityRegex().Replace(text, m =>
        {
            priority = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "urgent" or "u" or "1" or "!" => Models.Priority.Urgent,
                "high" or "h" or "2" => Models.Priority.High,
                "medium" or "med" or "m" or "3" => Models.Priority.Medium,
                _ => Models.Priority.Low,
            };
            return " ";
        });

        // ---- ISO date ----
        text = IsoDateRegex().Replace(text, m =>
        {
            if (DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                date = d;
            return " ";
        }, 1);

        // ---- relative words ----
        text = Replace(text, TonightRegex(), _ =>
        {
            date = now.Date;
            time ??= TimeSpan.FromHours(20);
        });
        text = Replace(text, TodayRegex(), _ => date = now.Date);
        text = Replace(text, TomorrowRegex(), _ => date = now.Date.AddDays(1));
        text = Replace(text, NextWeekRegex(), _ => date = NextWeekday(now.Date, DayOfWeek.Monday, forceNextWeek: false));
        text = Replace(text, WeekendRegex(), _ => date = NextWeekday(now.Date, DayOfWeek.Saturday, forceNextWeek: false, allowToday: true));
        text = Replace(text, InRegex(), m =>
        {
            var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value.ToLowerInvariant();
            date = unit.StartsWith('d') ? now.Date.AddDays(n)
                : unit.StartsWith('w') ? now.Date.AddDays(7 * n)
                : now.Date.AddMonths(n);
        });

        // ---- month + day ("jun 12", "12 june", "june 12th") ----
        text = Replace(text, MonthDayRegex(), m =>
        {
            if (TryMonth(m.Groups[1].Value, out var month)) date = MakeDate(now, month, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        });
        text = Replace(text, DayMonthRegex(), m =>
        {
            if (TryMonth(m.Groups[2].Value, out var month)) date = MakeDate(now, month, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        });

        // ---- weekdays ("fri", "next friday", "on monday") ----
        text = Replace(text, WeekdayRegex(), m =>
        {
            if (Days.TryGetValue(m.Groups[2].Value, out var dow))
                date = NextWeekday(now.Date, dow, forceNextWeek: m.Groups[1].Success && m.Groups[1].Value.Length > 0);
        });

        // ---- times ----
        text = Replace(text, NoonRegex(), m => time = m.Value.Trim().StartsWith("noon", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(12)
            : new TimeSpan(23, 59, 0));
        text = Replace(text, AmPmRegex(), m =>
        {
            var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) % 12;
            var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (m.Groups[3].Value.StartsWith("p", StringComparison.OrdinalIgnoreCase)) h += 12;
            if (h < 24 && min < 60) time = new TimeSpan(h, min, 0);
        });
        text = Replace(text, TwentyFourRegex(), m =>
        {
            var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (h < 24 && min < 60) time = new TimeSpan(h, min, 0);
        });
        text = Replace(text, AtHourRegex(), m =>
        {
            var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            // "at 5" means 5pm for small numbers during working hours.
            if (h is >= 1 and <= 7) h += 12;
            if (h < 24) time = TimeSpan.FromHours(h);
        });

        DateTime? due = null;
        if (date != null || time != null)
        {
            var d = date ?? now.Date;
            if (date == null && time != null && now.Date + time.Value < now) d = d.AddDays(1); // time already passed today
            due = d + (time ?? TimeSpan.Zero);
        }

        // ---- tidy the title ----
        var title = MultiSpaceRegex().Replace(text, " ").Trim();
        title = TrailingJoinerRegex().Replace(title, string.Empty).Trim();
        title = LeadingJoinerRegex().Replace(title, string.Empty).Trim();
        if (title.Length == 0) title = input?.Trim() ?? string.Empty;

        return new ParsedTask(title, due, priority, labels);
    }

    private static string Replace(string text, Regex regex, Action<Match> onMatch) =>
        regex.Replace(text, m =>
        {
            onMatch(m);
            return " ";
        }, 1);

    private static bool TryMonth(string s, out int month)
    {
        month = Array.IndexOf(MonthNames, s[..Math.Min(3, s.Length)].ToLowerInvariant()) + 1;
        return month > 0;
    }

    private static DateTime MakeDate(DateTime now, int month, int day)
    {
        day = Math.Clamp(day, 1, DateTime.DaysInMonth(now.Year, month));
        var d = new DateTime(now.Year, month, day);
        // A date that already passed this year means next year.
        return d < now.Date ? d.AddYears(1) : d;
    }

    private static DateTime NextWeekday(DateTime today, DayOfWeek dow, bool forceNextWeek, bool allowToday = false)
    {
        var d = allowToday ? today : today.AddDays(1);
        while (d.DayOfWeek != dow) d = d.AddDays(1);
        if (forceNextWeek && (d - today).TotalDays < 7) d = d.AddDays(7);
        return d;
    }

    [GeneratedRegex(@"(?<=\s)#([\p{L}\p{N}_\-]+)(?=\s)")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"(?<=\s)!(urgent|high|medium|med|low|u|h|m|l|[1-4]|!)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex PriorityRegex();

    [GeneratedRegex(@"(?<=\s)(\d{4}-\d{2}-\d{2})(?=\s)")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"(?<=\s)(?:on\s+|by\s+|due\s+)?tonight(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex TonightRegex();

    [GeneratedRegex(@"(?<=\s)(?:on\s+|by\s+|due\s+)?(?:today)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex TodayRegex();

    [GeneratedRegex(@"(?<=\s)(?:on\s+|by\s+|due\s+)?(?:tomorrow|tmrw|tmr)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex TomorrowRegex();

    [GeneratedRegex(@"(?<=\s)(?:by\s+)?next\s+week(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex NextWeekRegex();

    [GeneratedRegex(@"(?<=\s)(?:this\s+|on\s+the\s+|by\s+the\s+)?weekend(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex WeekendRegex();

    [GeneratedRegex(@"(?<=\s)in\s+(\d{1,3})\s+(days?|weeks?|months?)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex InRegex();

    [GeneratedRegex(@"(?<=\s)(?:on\s+|by\s+|due\s+)?(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDayRegex();

    [GeneratedRegex(@"(?<=\s)(?:on\s+|by\s+|due\s+)?(\d{1,2})(?:st|nd|rd|th)?\s+(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthRegex();

    [GeneratedRegex(@"(?<=\s)(?:on\s+|by\s+|due\s+)?(next\s+|this\s+)?(mon(?:day)?|tue(?:s(?:day)?)?|wed(?:nesday)?|thu(?:r(?:s(?:day)?)?)?|fri(?:day)?|sat(?:urday)?|sun(?:day)?)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex(@"(?<=\s)(?:at\s+)?(noon|midnight)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex NoonRegex();

    [GeneratedRegex(@"(?<=\s)(?:at\s+|@)?(\d{1,2})(?::(\d{2}))?\s?(am|pm|a|p)(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex AmPmRegex();

    [GeneratedRegex(@"(?<=\s)(?:at\s+|@)?([01]?\d|2[0-3]):([0-5]\d)(?=\s)")]
    private static partial Regex TwentyFourRegex();

    [GeneratedRegex(@"(?<=\s)(?:at|@)\s*(\d{1,2})(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex AtHourRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\s+(on|at|by|due|for)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingJoinerRegex();

    [GeneratedRegex(@"^(on|at|by|due)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingJoinerRegex();
}
