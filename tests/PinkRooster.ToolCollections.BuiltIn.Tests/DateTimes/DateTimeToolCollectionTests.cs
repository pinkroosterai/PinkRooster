using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.DateTimes;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.DateTimes;

public sealed class DateTimeToolCollectionTests
{
    private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone(
        "plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two");

    [Fact]
    public void HasAPublicParameterlessConstructor()
    {
        Assert.NotNull(typeof(DateTimeToolCollection).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public async Task GetContextAsync_ShowsTheCurrentDayDateAndTimeInTheDefaultTimeZone()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string? context = await collection.GetContextAsync(CancellationToken.None);

        Assert.Equal("## Current date and time\nThursday, 2026-09-24T14:30:00+02:00 (plus-two)", context);
    }

    [Fact]
    public void GetAIFunctions_HasNoCurrentDateTimeTool()
    {
        Assert.DoesNotContain(new DateTimeToolCollection().GetAIFunctions(), f => f.Name == "GetCurrentDateTime");
    }

    [Fact]
    public void Instructions_NameTheDefaultTimeZoneAndConstraintsForbidMentalDateArithmetic()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        Assert.Equal(["Dates and times are in plus-two unless the user names another time zone."], collection.Instructions);
        Assert.Contains(collection.Constraints, constraint => constraint.Contains("in your head"));
    }

    [Fact]
    public void GetDateTimeAfter_ZeroOffsetWithTimeZone_GivesTheCurrentTimeThere()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDateTimeAfter(days: 0, timeZoneId: "UTC");

        Assert.Equal("Thursday, 2026-09-24T12:30:00+00:00 (UTC)", result);
    }

    [Fact]
    public void GetDateTimeAfter_AddsDaysHoursAndMinutes()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDateTimeAfter(days: 3, hours: 2, minutes: 30);

        Assert.Equal("Sunday, 2026-09-27T17:00:00+02:00 (plus-two)", result);
    }

    [Fact]
    public void GetDateTimeAfter_AcceptsNegativeDays()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDateTimeAfter(days: -1);

        Assert.Equal("Wednesday, 2026-09-23T14:30:00+02:00 (plus-two)", result);
    }

    [Fact]
    public void GetDateTimeAfter_DaysKeepTheLocalTimeAcrossFallBack()
    {
        // Saturday 12:00 CEST; clocks go back to CET that night.
        DateTimeToolCollection collection = CreateAmsterdamCollection(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));

        string result = collection.GetDateTimeAfter(days: 1);

        Assert.Equal("Sunday, 2026-10-25T12:00:00+01:00 (Europe/Amsterdam)", result);
    }

    [Fact]
    public void GetDateTimeAfter_HoursAreElapsedTimeAcrossFallBack()
    {
        DateTimeToolCollection collection = CreateAmsterdamCollection(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));

        string result = collection.GetDateTimeAfter(days: 0, hours: 24);

        Assert.Equal("Sunday, 2026-10-25T11:00:00+01:00 (Europe/Amsterdam)", result);
    }

    [Fact]
    public void GetDateTimeAfter_SkippedLocalTimeMovesForwardByTheGap()
    {
        // Saturday 02:30 CET; 02:30 does not exist on Sunday because clocks jump to 03:00.
        DateTimeToolCollection collection = CreateAmsterdamCollection(new DateTimeOffset(2026, 3, 28, 1, 30, 0, TimeSpan.Zero));

        string result = collection.GetDateTimeAfter(days: 1);

        Assert.Equal("Sunday, 2026-03-29T03:30:00+02:00 (Europe/Amsterdam)", result);
    }

    [Fact]
    public void GetDateTimeAfter_RepeatedLocalTimeTakesTheFirstOccurrence()
    {
        // Saturday 02:30 CEST; 02:30 happens twice on Sunday.
        DateTimeToolCollection collection = CreateAmsterdamCollection(new DateTimeOffset(2026, 10, 24, 0, 30, 0, TimeSpan.Zero));

        string result = collection.GetDateTimeAfter(days: 1);

        Assert.Equal("Sunday, 2026-10-25T02:30:00+02:00 (Europe/Amsterdam)", result);
    }

    [Fact]
    public void GetDateTimeAfter_OutOfRangeOffset_ReturnsErrorString()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDateTimeAfter(days: int.MaxValue);

        Assert.StartsWith($"Error: The offset of {int.MaxValue} days", result);
        Assert.EndsWith("Use a smaller offset.", result);
    }

    [Fact]
    public void GetDateTimeAfter_InvalidTimeZone_ReturnsErrorString()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDateTimeAfter(days: 1, timeZoneId: "Not/AZone");

        Assert.StartsWith("Error: Unknown time zone 'Not/AZone'", result);
    }

    [Fact]
    public void GetDayOfWeek_KnownDate()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDayOfWeek("2026-09-24");

        Assert.Equal("Thursday", result);
    }

    [Fact]
    public void GetDayOfWeek_InvalidDate_ReturnsErrorString()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDayOfWeek("not-a-date");

        Assert.Equal("Error: Invalid date 'not-a-date'. Use the yyyy-MM-dd format, for example '2026-09-24'.", result);
    }

    [Fact]
    public void Constructor_InvalidDefaultTimeZone_Throws()
    {
        FixedTimeProvider provider = CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new DateTimeToolCollection(provider, "Not/AZone"));

        Assert.Equal("defaultTimeZone", exception.ParamName);
    }

    [Fact]
    public void GetAIFunctions_ExposesExpectedToolNames()
    {
        DateTimeToolCollection collection = new();

        IReadOnlyList<AIFunction> functions = collection.GetAIFunctions();

        Assert.Equal(3, functions.Count);
        Assert.Contains(functions, f => f.Name == "GetDaysBetween");
        Assert.Contains(functions, f => f.Name == "GetDateTimeAfter");
        Assert.Contains(functions, f => f.Name == "GetDayOfWeek");
    }

    [Fact]
    public void GetDateTimeAfter_OnlyHours_CountsFromNowWithoutDays()
    {
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 12, 30, 0, TimeSpan.Zero), PlusTwo));

        string result = collection.GetDateTimeAfter(hours: -36);

        Assert.Equal("Wednesday, 2026-09-23T02:30:00+02:00 (plus-two)", result);
    }

    [Fact]
    public void GetAIFunctions_OnlyTheDayOfWeekDateIsRequired()
    {
        DateTimeToolCollection collection = new();

        string after = Assert.Single(collection.GetAIFunctions(), f => f.Name == "GetDateTimeAfter").JsonSchema.GetRawText();
        string dayOfWeek = Assert.Single(collection.GetAIFunctions(), f => f.Name == "GetDayOfWeek").JsonSchema.GetRawText();

        Assert.DoesNotContain("\"required\"", after);
        Assert.Contains("\"required\"", dayOfWeek);
    }

    [Fact]
    public void GetDaysBetween_EndAfterStart_SaysHowManyDaysAfter()
    {
        string result = new DateTimeToolCollection().GetDaysBetween("2026-12-25", "2026-09-24");

        Assert.Equal("2026-12-25 (Friday) is 92 days after 2026-09-24 (Thursday).", result);
    }

    [Fact]
    public void GetDaysBetween_EndBeforeStart_SaysHowManyDaysBefore()
    {
        string result = new DateTimeToolCollection().GetDaysBetween("2026-09-23", "2026-09-24");

        Assert.Equal("2026-09-23 (Wednesday) is 1 day before 2026-09-24 (Thursday).", result);
    }

    [Fact]
    public void GetDaysBetween_SameDate_SaysSameDay()
    {
        string result = new DateTimeToolCollection().GetDaysBetween("2026-09-24", "2026-09-24");

        Assert.Equal("2026-09-24 (Thursday) is the same day as 2026-09-24 (Thursday).", result);
    }

    [Fact]
    public void GetDaysBetween_NoStartDate_CountsFromTodayInTheDefaultZone()
    {
        // 22:30 UTC is already Friday 25 September in the plus-two zone.
        DateTimeToolCollection collection = new(CreateProvider(new DateTimeOffset(2026, 9, 24, 22, 30, 0, TimeSpan.Zero), PlusTwo));

        Assert.Equal("2026-09-30 (Wednesday) is 5 days after 2026-09-25 (Friday).", collection.GetDaysBetween("2026-09-30"));
        Assert.Equal("2026-09-30 (Wednesday) is 6 days after 2026-09-24 (Thursday).", collection.GetDaysBetween("2026-09-30", timeZoneId: "UTC"));
    }

    [Theory]
    [InlineData("not-a-date", "2026-09-24", "not-a-date")]
    [InlineData("2026-12-25", "24 September 2026", "24 September 2026")]
    public void GetDaysBetween_InvalidDate_ReturnsErrorNamingIt(string endDate, string startDate, string named)
    {
        string result = new DateTimeToolCollection().GetDaysBetween(endDate, startDate);

        Assert.Equal($"Error: Invalid date '{named}'. Use the yyyy-MM-dd format, for example '2026-09-24'.", result);
    }

    [Fact]
    public void GetDaysBetween_UnknownTimeZone_ReturnsError()
    {
        string result = new DateTimeToolCollection().GetDaysBetween("2026-12-25", timeZoneId: "Not/AZone");

        Assert.StartsWith("Error: Unknown time zone 'Not/AZone'", result);
    }

    [Fact]
    public void GetDaysBetween_Schema_RequiresOnlyTheEndDateAndTheOtherToolsPointAtIt()
    {
        IReadOnlyList<AIFunction> functions = new DateTimeToolCollection().GetAIFunctions();
        AIFunction between = Assert.Single(functions, f => f.Name == "GetDaysBetween");

        Assert.Contains("\"required\":[\"endDate\"]", between.JsonSchema.GetRawText().Replace(" ", ""));
        Assert.Contains("GetDaysBetween", Assert.Single(functions, f => f.Name == "GetDateTimeAfter").Description);
        Assert.Contains("GetDaysBetween", Assert.Single(functions, f => f.Name == "GetDayOfWeek").Description);
    }

    private static DateTimeToolCollection CreateAmsterdamCollection(DateTimeOffset utcNow) =>
        new(CreateProvider(utcNow, TimeZoneInfo.Utc), "Europe/Amsterdam");

    private static FixedTimeProvider CreateProvider(DateTimeOffset utcNow, TimeZoneInfo zone) => new(utcNow, zone);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
