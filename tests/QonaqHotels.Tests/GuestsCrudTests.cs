using QonaqHotels.Pages.Admin.Guests;

namespace QonaqHotels.Tests;

// RED: verify Guests page model types exist with expected shape
public class GuestsCrudTests
{
    [Fact]
    public void GuestRow_HasRequiredProperties()
    {
        var t = typeof(GuestRow);
        Assert.NotNull(t.GetProperty("Id"));
        Assert.NotNull(t.GetProperty("FullName"));
        Assert.NotNull(t.GetProperty("DocumentNumber"));
        Assert.NotNull(t.GetProperty("Phone"));
        Assert.NotNull(t.GetProperty("CountryName"));
        Assert.NotNull(t.GetProperty("IsActive"));
    }

    [Fact]
    public void CreateModel_HasRequiredBindProperties()
    {
        var t = typeof(CreateModel);
        Assert.NotNull(t.GetProperty("FullName"));
        Assert.NotNull(t.GetProperty("DocumentNumber"));
        Assert.NotNull(t.GetProperty("BirthDate"));
        Assert.NotNull(t.GetProperty("Phone"));
        Assert.NotNull(t.GetProperty("Email"));
        Assert.NotNull(t.GetProperty("CountryId"));
    }

    [Fact]
    public void EditModel_HasIsActiveProperty()
    {
        var prop = typeof(EditModel).GetProperty("IsActive");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop.PropertyType);
    }

    [Fact]
    public void DeleteModel_HasBookingCountProperty()
    {
        var prop = typeof(DeleteModel).GetProperty("BookingCount");
        Assert.NotNull(prop);
        Assert.Equal(typeof(int), prop.PropertyType);
    }
}
