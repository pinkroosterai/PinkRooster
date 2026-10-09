using System.ComponentModel;
using System.Globalization;

namespace PinkRooster.ToolCollections.BuiltIn.DateTimes;

/// <summary>
/// Exposes date and time helpers as AI function tools, using an injectable <see cref="TimeProvider"/>
/// so callers can control the clock in tests.
/// </summary>
public sealed class DateTimeToolCollection : ToolCollection
{
    private readonly TimeProvider timeProvider;
    private readonly TimeZoneInfo defaultTimeZone;

    /// <summary>Creates a collection that reads the system clock and answers in the local time zone; an agent class names it in <c>[AgentTools]</c> through this constructor.</summary>
    public DateTimeToolCollection() : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DateTimeToolCollection"/> class.
    /// </summary>
    /// <param name="timeProvider">The clock to read from. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="defaultTimeZone">The IANA or Windows time zone ID used when a tool call does not specify one. Defaults to the provider's local time zone.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="defaultTimeZone"/> is not a known time zone ID on this system.</exception>
    public DateTimeToolCollection(TimeProvider? timeProvider, string? defaultTimeZone = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.defaultTimeZone = ResolveDefaultTimeZone(defaultTimeZone) ?? this.timeProvider.LocalTimeZone;

        AddInstruction($"Dates and times are in {this.defaultTimeZone.Id} unless the user names another time zone.");
        AddConstraint("Don't work out dates, weekdays or date offsets in your head; calendar arithmetic from memory is unreliable.");
    }



    /// <summary>Returns the current day of the week, date and time in the default time zone under a heading.</summary>
    /// <remarks>An agent sends it before every model call, so the model always knows the time without calling a tool.</remarks>
    public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), defaultTimeZone);
        return ValueTask.FromResult<string?>($"## Current date and time\n{Format(now, defaultTimeZone)}");
    }

    /// <summary>The <c>GetDateTimeAfter</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("Gets the day of the week, date and time a given number of days, hours and minutes from now, for example " +
        "'Thursday, 2026-09-24T14:30:00+02:00 (Europe/Amsterdam)'. Use it for 'what date is it in 10 days' or '36 hours ago' (negative values go back), " +
        "or with 0 for everything to get the current time in another time zone; the current time in the default zone is already in your context. " +
        "For the weekday of a given date use GetDayOfWeek, and for the days between two dates use GetDaysBetween. " +
        "Days keep the same local time of day across daylight-saving changes; hours and minutes are elapsed time.",
        Kind = ToolKind.Read)]
    public string GetDateTimeAfter(
        [Description("Whole days to add; negative goes back.")] int days = 0,
        [Description("Hours of elapsed time to add after the days; negative goes back.")] int hours = 0,
        [Description("Minutes of elapsed time to add after the days; negative goes back.")] int minutes = 0,
        [Description("Optional time zone to answer in, as an IANA ID such as 'America/New_York' (Windows IDs also work). Leave it out for the default zone.")] string? timeZoneId = null)
    {
        string? error = TryResolveTimeZone(timeZoneId, out TimeZoneInfo zone);
        if (error is not null)
        {
            return error;
        }

        try
        {
            // Days move the local calendar date so the wall-clock time survives a DST change;
            // hours and minutes are elapsed time.
            DateTimeOffset nowLocal = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
            DateTimeOffset afterDays = FromLocalTime(nowLocal.DateTime.AddDays(days), zone);
            DateTimeOffset result = TimeZoneInfo.ConvertTime(afterDays.AddHours(hours).AddMinutes(minutes), zone);
            return Format(result, zone);
        }
        catch (ArgumentOutOfRangeException)
        {
            return $"Error: The offset of {days} days, {hours} hours, and {minutes} minutes is outside the supported date range. Use a smaller offset.";
        }
    }

    /// <summary>The <c>GetDayOfWeek</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("Gets the day of the week, for example 'Thursday', for a specific calendar date. Use it when the user names a date and " +
        "asks which weekday it falls on; for dates relative to now use GetDateTimeAfter, and for the distance between dates use " +
        "GetDaysBetween. It doesn't depend on the time zone.",
        Kind = ToolKind.Read)]
    public string GetDayOfWeek(
        [Description("The calendar date in yyyy-MM-dd format, for example '2026-09-24'. Convert other formats, such as '24 September 2026', before calling.")] string date)
    {
        if (!TryParseDate(date, out DateOnly parsedDate))
        {
            return InvalidDate(date);
        }

        return parsedDate.DayOfWeek.ToString();
    }

    /// <summary>The <c>GetDaysBetween</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("Gets the number of whole days from one calendar date to another, for example '2026-12-25 (Friday) is 92 days after " +
        "2026-09-24 (Thursday).' Use it for 'how many days until' or 'how long since' questions and for the gap between two dates. " +
        "Leave startDate out to count from today. For a date a number of days from now use GetDateTimeAfter instead.",
        Kind = ToolKind.Read)]
    public string GetDaysBetween(
        [Description("The date to count to, in yyyy-MM-dd format, for example '2026-12-25'. Convert other formats, such as '25 December 2026', before calling.")] string endDate,
        [Description("The date to count from, in yyyy-MM-dd format. Leave it out to count from today.")] string? startDate = null,
        [Description("Optional time zone that decides what today is when startDate is left out, as an IANA ID such as 'America/New_York'. Leave it out for the default zone.")] string? timeZoneId = null)
    {
        if (!TryParseDate(endDate, out DateOnly end))
        {
            return InvalidDate(endDate);
        }

        DateOnly start;
        if (string.IsNullOrWhiteSpace(startDate))
        {
            string? error = TryResolveTimeZone(timeZoneId, out TimeZoneInfo zone);
            if (error is not null)
            {
                return error;
            }

            start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
        }
        else if (!TryParseDate(startDate, out start))
        {
            return InvalidDate(startDate);
        }

        int days = end.DayNumber - start.DayNumber;
        string from = $"{start:yyyy-MM-dd} ({start.DayOfWeek})";
        string to = $"{end:yyyy-MM-dd} ({end.DayOfWeek})";
        return days switch
        {
            0 => $"{to} is the same day as {from}.",
            1 or -1 => $"{to} is 1 day {(days > 0 ? "after" : "before")} {from}.",
            _ => $"{to} is {Math.Abs(days)} days {(days > 0 ? "after" : "before")} {from}."
        };
    }

    private static bool TryParseDate(string? date, out DateOnly parsedDate) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);

    private static string InvalidDate(string? date) =>
        $"Error: Invalid date '{date}'. Use the yyyy-MM-dd format, for example '2026-09-24'.";

    private static TimeZoneInfo? ResolveDefaultTimeZone(string? defaultTimeZone)
    {
        if (string.IsNullOrWhiteSpace(defaultTimeZone))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(defaultTimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException(
                $"Unknown time zone '{defaultTimeZone}'. Use an IANA ID such as 'Europe/Amsterdam' or a Windows ID such as 'W. Europe Standard Time'.",
                nameof(defaultTimeZone));
        }
        catch (InvalidTimeZoneException)
        {
            throw new ArgumentException(
                $"The time zone '{defaultTimeZone}' is not valid on this system.",
                nameof(defaultTimeZone));
        }
    }

    private string? TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        zone = defaultTimeZone;

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return null;
        }
        catch (TimeZoneNotFoundException)
        {
            return $"Error: Unknown time zone '{timeZoneId}'. Use an IANA ID such as 'Europe/Amsterdam' or a Windows ID such as 'W. Europe Standard Time'.";
        }
        catch (InvalidTimeZoneException)
        {
            return $"Error: The time zone '{timeZoneId}' is not valid on this system.";
        }
    }

    // A time skipped by a spring-forward change is read with the offset from before the change,
    // which moves it forward by the gap; a time repeated by a fall-back change takes its first occurrence.
    private static DateTimeOffset FromLocalTime(DateTime localTime, TimeZoneInfo zone)
    {
        TimeSpan offset;
        if (zone.IsInvalidTime(localTime))
        {
            offset = zone.GetUtcOffset(localTime.AddDays(-1));
        }
        else if (zone.IsAmbiguousTime(localTime))
        {
            offset = zone.GetAmbiguousTimeOffsets(localTime).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(localTime);
        }

        return new DateTimeOffset(localTime, offset);
    }

    private static string Format(DateTimeOffset value, TimeZoneInfo zone) =>
        $"{value.ToString("dddd, yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)} ({zone.Id})";
}
