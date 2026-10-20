namespace QonaqHotels.Tests;

// RED: verify OnGetSearchAsync handlers accept DateOnly? date-range parameters
public class DateFilterTests
{
    // --- Bookings ---

    [Fact]
    public void BookingsSearch_HasCheckinFromParameter()
    {
        var method = typeof(QonaqHotels.Pages.Admin.Bookings.IndexModel).GetMethod("OnGetSearchAsync");
        var param = method?.GetParameters().FirstOrDefault(p => p.Name == "checkinFrom");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateOnly?), param.ParameterType);
    }

    [Fact]
    public void BookingsSearch_HasCheckinToParameter()
    {
        var method = typeof(QonaqHotels.Pages.Admin.Bookings.IndexModel).GetMethod("OnGetSearchAsync");
        var param = method?.GetParameters().FirstOrDefault(p => p.Name == "checkinTo");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateOnly?), param.ParameterType);
    }

    [Fact]
    public void BookingsSearch_HasCheckoutFromParameter()
    {
        var method = typeof(QonaqHotels.Pages.Admin.Bookings.IndexModel).GetMethod("OnGetSearchAsync");
        var param = method?.GetParameters().FirstOrDefault(p => p.Name == "checkoutFrom");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateOnly?), param.ParameterType);
    }

    [Fact]
    public void BookingsSearch_HasCheckoutToParameter()
    {
        var method = typeof(QonaqHotels.Pages.Admin.Bookings.IndexModel).GetMethod("OnGetSearchAsync");
        var param = method?.GetParameters().FirstOrDefault(p => p.Name == "checkoutTo");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateOnly?), param.ParameterType);
    }

    // --- Employees ---

    [Fact]
    public void EmployeesSearch_HasHireDateFromParameter()
    {
        var method = typeof(QonaqHotels.Pages.Admin.Employees.IndexModel).GetMethod("OnGetSearchAsync");
        var param = method?.GetParameters().FirstOrDefault(p => p.Name == "hireDateFrom");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateOnly?), param.ParameterType);
    }

    [Fact]
    public void EmployeesSearch_HasHireDateToParameter()
    {
        var method = typeof(QonaqHotels.Pages.Admin.Employees.IndexModel).GetMethod("OnGetSearchAsync");
        var param = method?.GetParameters().FirstOrDefault(p => p.Name == "hireDateTo");
        Assert.NotNull(param);
        Assert.Equal(typeof(DateOnly?), param.ParameterType);
    }
}
