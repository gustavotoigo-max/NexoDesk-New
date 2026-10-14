using System.Net;
using System.Net.Http;
using NexoDesk.App.Services;
using NexoDesk.App.ViewModels;

namespace NexoDesk.Windows.Tests;

public class ReleasePollingTests
{
    private sealed class HeadHandler : HttpMessageHandler
    {
        public string Tag = "v1.4.0";
        public int Requests;
        public HttpStatusCode Status = HttpStatusCode.Found;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            Assert.EndsWith("/releases/latest", request.RequestUri!.AbsoluteUri);
            Requests++;
            var response = new HttpResponseMessage(Status);
            response.Headers.Location = new Uri("https://github.com/gustavotoigo-max/NexoDesk-New/releases/tag/" + Tag);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task CurrentReleaseNeedsOnlyHeadersAndDoesNotUseApi()
    {
        var handler = new HeadHandler();
        using var http = new HttpClient(handler);
        var poller = new ReleasePoller(http, new(1, 4, 0), _ => throw new InvalidOperationException("API desnecessária."));
        for (int i = 0; i < 3; i++)
            Assert.Equal(UpdateCheckState.UpToDate, (await poller.CheckAsync(default)).State);
        Assert.Equal(3, handler.Requests);
        Assert.Equal(TimeSpan.FromMinutes(1), UpdateViewModel.CheckInterval);
    }

    [Fact]
    public async Task RepeatedPollsCacheValidatedMetadataButNewTagInvalidatesIt()
    {
        var handler = new HeadHandler();
        using var http = new HttpClient(handler);
        int apiCalls = 0;
        var poller = new ReleasePoller(http, new(1, 3, 1), _ =>
        {
            apiCalls++;
            var info = new UpdateInfo(Version.Parse(handler.Tag[1..]), handler.Tag, "https://github.com/test", "Notas", new string('a', 64));
            return Task.FromResult(new UpdateCheckResult(UpdateCheckState.Available, info, "Disponível"));
        });
        Assert.Equal("v1.4.0", (await poller.CheckAsync(default)).Info!.Tag);
        await poller.CheckAsync(default);
        Assert.Equal(1, apiCalls);
        handler.Tag = "v1.4.1";
        Assert.Equal("v1.4.1", (await poller.CheckAsync(default)).Info!.Tag);
        Assert.Equal(2, apiCalls);
    }

    [Fact]
    public async Task FailedMetadataIsRetriedAndHttpFailureDoesNotOfferCachedRelease()
    {
        var handler = new HeadHandler();
        using var http = new HttpClient(handler);
        int calls = 0;
        var poller = new ReleasePoller(http, new(1, 3, 1), _ =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckState.Failed, null, "Falha"));
        });
        await poller.CheckAsync(default);
        await poller.CheckAsync(default);
        Assert.Equal(2, calls);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        Assert.Null((await poller.CheckAsync(default)).Info);
        Assert.Equal(2, calls);
    }
}
