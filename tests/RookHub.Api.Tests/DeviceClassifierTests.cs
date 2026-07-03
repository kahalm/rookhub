using RookHub.Api.Logging;
using Xunit;

namespace RookHub.Api.Tests;

public class DeviceClassifierTests
{
    [Theory]
    // Mobile
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", DeviceClassifier.Mobile)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Mobile Safari/537.36", DeviceClassifier.Mobile)]
    // RookHub Android TWA (Chrome-Mobile-UA)
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-G991B; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/125.0 Mobile Safari/537.36", DeviceClassifier.Mobile)]
    [InlineData("Mozilla/5.0 (Windows Phone 10.0; Android 6.0) IEMobile/11.0", DeviceClassifier.Mobile)]
    public void ClassifiesMobile(string ua, string expected) => Assert.Equal(expected, DeviceClassifier.Classify(ua));

    [Theory]
    // iPad
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/604.1", DeviceClassifier.Tablet)]
    // Android tablet (kein „Mobile" im UA)
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36", DeviceClassifier.Tablet)]
    [InlineData("Mozilla/5.0 (Linux; U; Android 4.4; KFTHWI Build) AppleWebKit/537.36 Silk/47.1", DeviceClassifier.Tablet)]
    public void ClassifiesTablet(string ua, string expected) => Assert.Equal(expected, DeviceClassifier.Classify(ua));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36", DeviceClassifier.Desktop)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15", DeviceClassifier.Desktop)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36", DeviceClassifier.Desktop)]
    public void ClassifiesDesktop(string ua, string expected) => Assert.Equal(expected, DeviceClassifier.Classify(ua));

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", DeviceClassifier.Bot)]
    [InlineData("curl/8.4.0", DeviceClassifier.Bot)]
    [InlineData("python-requests/2.31.0", DeviceClassifier.Bot)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) HeadlessChrome/126.0", DeviceClassifier.Bot)]
    public void ClassifiesBot(string ua, string expected) => Assert.Equal(expected, DeviceClassifier.Classify(ua));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrNull_IsUnknown(string? ua) => Assert.Equal(DeviceClassifier.Unknown, DeviceClassifier.Classify(ua));
}
