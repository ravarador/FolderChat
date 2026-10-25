using System.Net;
using FolderChat.Tests.Fakes;
using FolderChat.Web.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace FolderChat.Tests.Web;

public sealed class FolderPickerTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.20", false)]
    public void The_folder_window_only_opens_for_a_browser_on_this_computer(string address, bool opens)
    {
        // It opens on the computer running FolderChat, where someone on another device couldn't see it.
        var context = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Parse(address) } };

        Assert.Equal(opens, new FakeFolderPicker().CanOpenFor(context));
    }

    [Fact]
    public void The_folder_window_needs_a_desktop()
    {
        var context = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Loopback } };

        Assert.False(new FakeFolderPicker { IsAvailable = false }.CanOpenFor(context));
    }
}
