using PzManager.Domain.Devices;
using Xunit;

namespace PzManager.Domain.Tests.Devices;

public sealed class DeviceIdTests
{
    [Fact]
    public void New_ReturnsNonEmptyValue()
    {
        var id = DeviceId.New();
        Assert.False(string.IsNullOrWhiteSpace(id.Value));
    }

    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        var id = new DeviceId("abc");
        Assert.Equal("abc", id.ToString());
    }
}

