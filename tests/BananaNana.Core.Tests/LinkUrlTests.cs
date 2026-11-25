using Xunit;

namespace BananaNana.Tests;

public class LinkUrlTests
{
    [Theory]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("example.com/oferta", "https://example.com/oferta")]
    [InlineData("  http://intranet.local  ", "http://intranet.local/")]
    [InlineData("jan@example.com", "mailto:jan@example.com")]
    [InlineData("mailto:jan@example.com", "mailto:jan@example.com")]
    public void Accepts_web_and_mail_addresses(string input, string expected) =>
        Assert.Equal(expected, LinkUrl.TryNormalize(input)?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("hello world")]
    [InlineData("localhostonly")]
    public void Rejects_everything_else(string input) => Assert.Null(LinkUrl.TryNormalize(input));
}
