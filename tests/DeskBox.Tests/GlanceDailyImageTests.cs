using System.Net;
using System.Text;
using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;
using Xunit;

namespace DeskBox.Tests;

public sealed class GlanceDailyImageTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "DeskBoxDailyTests", Guid.NewGuid().ToString("N"));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Image() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
    private static GlanceWidgetData Daily() => new() { BackgroundSource = GlanceBackgroundSource.Bing, BingDaily = true };
    [Fact]
    public async Task Daily_OnlyQueriesLatestAndDownloadsOncePerIdentity()
    {
        int archive = 0, downloads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("HPImageArchive"))
            {
                archive++; Assert.Contains("idx=0&n=1", request.RequestUri.Query);
                return Json(new { images = new[] { new { url = "/today.jpg", hsh = "today", startdate = "20261006", wp = true } } });
            }
            downloads++; return Image();
        }));
        var service = new GlanceImageService(_cache, http, () => true);
        var first = Assert.Single(await service.RefreshOnlineImagesAsync(Daily()));
        var second = Assert.Single(await service.RefreshOnlineImagesAsync(Daily()));
        Assert.Equal(new DateOnly(2026, 10, 6), second.PublishedDate);
        Assert.Equal(first.Id, second.Id); Assert.Equal(2, archive); Assert.Equal(1, downloads);
        Assert.True(service.LastSuccessfulDailyBingRefreshUtc > default(DateTimeOffset));
    }
    [Fact]
    public async Task Daily_NewDayDownloadsDespiteFullHistoryAndRetainsOfflineCache()
    {
        string date = "20261006";
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.Contains("HPImageArchive")
            ? Json(new { images = new[] { new { url = $"/{date}.jpg", hsh = date, startdate = date } } }) : Image()));
        var service = new GlanceImageService(_cache, http, () => true);
        for (int day = 1; day <= 20; day++)
        {
            date = $"202610{day:00}";
            var images = await service.RefreshOnlineImagesAsync(Daily());
            Assert.Equal(new DateOnly(2026, 10, day), images[0].PublishedDate);
        }
        using var offlineHttp = new HttpClient(new Handler(_ => throw new InvalidOperationException("Offline must not send a request.")));
        var offline = new GlanceImageService(_cache, offlineHttp, () => false);
        var cached = await offline.RefreshOnlineImagesAsync(Daily());
        Assert.Equal(18, cached.Count); Assert.Equal(new DateOnly(2026, 10, 20), cached[0].PublishedDate);
        Assert.True(File.Exists(cached[0].LocalPath));
    }
    [Fact]
    public async Task Daily_ReusedImageUpdatesPublicationDateAndSurvivesFullCacheTrimming()
    {
        string date = "20261001", identity = date;
        int downloads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("HPImageArchive"))
                return Json(new { images = new[] { new { url = $"/{identity}.jpg", hsh = identity, startdate = date } } });
            downloads++; return Image();
        }));
        var service = new GlanceImageService(_cache, http, () => true);
        for (int day = 1; day <= 18; day++)
        {
            date = identity = $"202610{day:00}";
            await service.RefreshOnlineImagesAsync(Daily());
        }
        identity = "20261001"; date = "20261019";
        var reused = await service.RefreshOnlineImagesAsync(Daily());
        Assert.Equal(new DateOnly(2026, 10, 19), reused[0].PublishedDate);
        Assert.Equal(18, reused.Count); Assert.Equal(18, downloads);
    }
    [Fact]
    public async Task Daily_FailedRefreshKeepsLastSuccessfulFile()
    {
        bool fail = false;
        using var http = new HttpClient(new Handler(request => fail ? new(HttpStatusCode.ServiceUnavailable) :
            request.RequestUri!.AbsolutePath.Contains("HPImageArchive")
                ? Json(new { images = new[] { new { url = "/today.jpg", hsh = "today", startdate = "20261006" } } }) : Image()));
        var service = new GlanceImageService(_cache, http, () => true);
        var first = Assert.Single(await service.RefreshOnlineImagesAsync(Daily()));
        fail = true;
        var checkedAt = service.LastSuccessfulDailyBingRefreshUtc;
        var fallback = Assert.Single(await service.RefreshOnlineImagesAsync(Daily()));
        Assert.Equal(first.Id, fallback.Id); Assert.True(File.Exists(fallback.LocalPath));
        Assert.Equal(checkedAt, service.LastSuccessfulDailyBingRefreshUtc);
    }
    public void Dispose() { if (Directory.Exists(_cache)) Directory.Delete(_cache, true); }
}
