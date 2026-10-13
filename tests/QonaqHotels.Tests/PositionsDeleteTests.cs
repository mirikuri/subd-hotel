using QonaqHotels.Pages.Admin.Positions;

namespace QonaqHotels.Tests;

// RED: verify DeleteModel exists and has expected shape
public class PositionsDeleteTests
{
    [Fact]
    public void DeleteModel_HasPositionNameProperty()
    {
        var prop = typeof(DeleteModel).GetProperty("PositionName");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop.PropertyType);
    }

    [Fact]
    public void DeleteModel_HasRoleProperty()
    {
        var prop = typeof(DeleteModel).GetProperty("Role");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop.PropertyType);
    }

    [Fact]
    public void DeleteModel_HasEmployeeCountProperty()
    {
        var prop = typeof(DeleteModel).GetProperty("EmployeeCount");
        Assert.NotNull(prop);
        Assert.Equal(typeof(int), prop.PropertyType);
    }
}
