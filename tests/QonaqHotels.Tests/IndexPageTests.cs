using Microsoft.AspNetCore.Hosting;
using Moq;
using QonaqHotels.Data;
using QonaqHotels.Pages;

namespace QonaqHotels.Tests;

public class IndexPageTests
{
    private static Db CreateDb() => TestDb.Create();

    private static Mock<IWebHostEnvironment> MockEnv(string webRoot = "/fake")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.WebRootPath).Returns(webRoot);
        return env;
    }

    [Fact]
    public async Task OnGetAsync_DoesNotThrow()
    {
        var model = new IndexModel(CreateDb(), MockEnv().Object);
        await model.OnGetAsync();
        Assert.NotNull(model.Hotels);
        Assert.NotNull(model.RoomTypes);
        Assert.NotNull(model.Cities);
    }

    [Fact]
    public void RoomImage_FileMissing_ReturnsPlaceholder()
    {
        var model = new IndexModel(CreateDb(), MockEnv("/nonexistent-dir").Object);
        Assert.Equal("/img/placeholder.webp", model.RoomImage(99));
    }

    [Fact]
    public async Task OnGetAsync_RoomTypesMaxSix()
    {
        var model = new IndexModel(CreateDb(), MockEnv().Object);
        await model.OnGetAsync();
        Assert.True(model.RoomTypes.Count <= 6);
    }
}
