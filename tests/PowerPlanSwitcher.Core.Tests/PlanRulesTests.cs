using PowerPlanSwitcher.Core;
using Xunit;

namespace PowerPlanSwitcher.Core.Tests;

public sealed class PlanRulesTests
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    [Fact]
    public void Defaults_MapAllFourCells()
    {
        var rules = new PlanRules();

        Assert.Equal(Balanced, rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Equal(PowerSaver, rules.Get(Activity.Active, PowerSource.Battery));
        Assert.Equal(PowerSaver, rules.Get(Activity.Idle, PowerSource.Ac));
        Assert.Equal(PowerSaver, rules.Get(Activity.Idle, PowerSource.Battery));
    }

    [Fact]
    public void Set_RoundTripsThroughGet()
    {
        var rules = new PlanRules();
        var highPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        rules.Set(Activity.Idle, PowerSource.Ac, highPerformance);

        Assert.Equal(highPerformance, rules.Get(Activity.Idle, PowerSource.Ac));
        Assert.Equal(Balanced, rules.Get(Activity.Active, PowerSource.Ac));
    }

    [Fact]
    public void Set_Null_ClearsCellAndReportsUnsetSlot()
    {
        var rules = new PlanRules();

        rules.Set(Activity.Active, PowerSource.Ac, null);

        Assert.Null(rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Contains(PlanSlot.AcActive, rules.UnsetSlots);
        Assert.DoesNotContain(PlanSlot.AcIdle, rules.UnsetSlots);
    }

    [Fact]
    public void UnsetSlots_IsEmptyWhenAllFourCellsSet()
    {
        Assert.Empty(new PlanRules().UnsetSlots);
    }

    [Fact]
    public void EmptyGuid_NormalizesToNull()
    {
        var rules = new PlanRules();
        rules.Set(Activity.Active, PowerSource.Ac, Guid.Empty);

        Assert.Null(rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Contains(PlanSlot.AcActive, rules.UnsetSlots);
    }

    [Fact]
    public void Get_DirectEmptyBackingField_ReturnsNullAndReportsUnset()
    {
        var rules = new PlanRules
        {
            AcActive = Guid.Empty,
        };

        Assert.Null(rules.Get(Activity.Active, PowerSource.Ac));
        Assert.Contains(PlanSlot.AcActive, rules.UnsetSlots);
    }

    [Fact]
    public void Set_InvalidPair_Throws()
    {
        var rules = new PlanRules();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            rules.Set((Activity)99, PowerSource.Ac, null));
    }

    [Fact]
    public void Get_InvalidPair_Throws()
    {
        var rules = new PlanRules();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            rules.Get((Activity)99, PowerSource.Ac));
    }
}
