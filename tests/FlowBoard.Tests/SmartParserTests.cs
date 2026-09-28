using FlowBoard.Models;
using FlowBoard.Services;
using Xunit;

namespace FlowBoard.Tests;

public class SmartParserTests
{
    // Monday 28 Sep 2026, 10:00
    private static readonly DateTime Now = new(2026, 9, 28, 10, 0, 0);

    [Fact]
    public void FullExample()
    {
        var p = SmartParser.Parse("Edit trailer fri 3pm #video !high", Now);
        Assert.Equal("Edit trailer", p.Title);
        Assert.Equal(new DateTime(2026, 10, 2, 15, 0, 0), p.Due);
        Assert.Equal(Priority.High, p.Priority);
        Assert.Equal(["video"], p.Labels);
    }

    [Theory]
    [InlineData("Call mom today", 2026, 9, 28, 0, 0)]
    [InlineData("Call mom tomorrow at 17:30", 2026, 9, 29, 17, 30)]
    [InlineData("Call mom tonight", 2026, 9, 28, 20, 0)]
    [InlineData("Pay rent on oct 1", 2026, 10, 1, 0, 0)]
    [InlineData("Pay rent 1st october", 2026, 10, 1, 0, 0)]
    [InlineData("Plan trip in 3 days", 2026, 10, 1, 0, 0)]
    [InlineData("Plan trip next week", 2026, 10, 5, 0, 0)]
    [InlineData("Clean garage this weekend", 2026, 10, 3, 0, 0)]
    [InlineData("Standup next monday 9am", 2026, 10, 5, 9, 0)]
    [InlineData("Lunch noon", 2026, 9, 28, 12, 0)]
    [InlineData("Release 2026-12-24", 2026, 12, 24, 0, 0)]
    public void Dates(string input, int y, int mo, int d, int h, int mi)
    {
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), SmartParser.Parse(input, Now).Due);
    }

    [Fact]
    public void PastTimeMeansTomorrow()
    {
        Assert.Equal(new DateTime(2026, 9, 29, 9, 0, 0), SmartParser.Parse("Gym 9am", Now).Due);
    }

    [Fact]
    public void PastMonthDayMeansNextYear()
    {
        Assert.Equal(new DateTime(2027, 1, 15), SmartParser.Parse("Taxes jan 15", Now).Due);
    }

    [Fact]
    public void PlainTextIsUntouched()
    {
        var p = SmartParser.Parse("Call Tom about the sunset photos", Now);
        Assert.Equal("Call Tom about the sunset photos", p.Title);
        Assert.Null(p.Due);
        Assert.Null(p.Priority);
        Assert.Empty(p.Labels);
    }

    [Fact]
    public void PriorityShortcutsAndMultipleLabels()
    {
        var p = SmartParser.Parse("Fix crash !1 #bug #ui_polish", Now);
        Assert.Equal(Priority.Urgent, p.Priority);
        Assert.Equal(["bug", "ui polish"], p.Labels);
        Assert.Equal("Fix crash", p.Title);
    }
}
