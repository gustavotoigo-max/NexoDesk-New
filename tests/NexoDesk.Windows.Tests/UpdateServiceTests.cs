using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using NexoDesk.App.Services;

namespace NexoDesk.Windows.Tests;

public class UpdateServiceTests
{
    private sealed class ReplyHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private static string Release(string version, string digest, string? url=null) => JsonSerializer.Serialize(new
    {
        tag_name = "v"+version, draft=false, prerelease=false, body="Notas",
        assets=new[] { new { name=$"NexoDesk-New-Setup-{version}.exe", digest,
            browser_download_url=url ?? $"https://github.com/gustavotoigo-max/NexoDesk-New/releases/download/v{version}/NexoDesk-New-Setup-{version}.exe" } }
    });

    [Fact]
    public async Task ValidNewRelease_IsAvailable()
    {
        using var http=new HttpClient(new ReplyHandler(HttpStatusCode.OK,Release("1.1.0","sha256:"+new string('a',64))));
        UpdateCheckResult result=await UpdateService.CheckAsync(http,new(1,0,0));
        Assert.Equal(UpdateCheckState.Available,result.State);
        Assert.Equal(new Version(1,1,0),result.Info!.Version);
    }

    [Fact]
    public async Task CurrentVersion_IsReportedAsUpToDate()
    {
        using var http=new HttpClient(new ReplyHandler(HttpStatusCode.OK,Release("1.1.0","sha256:"+new string('a',64))));
        Assert.Equal(UpdateCheckState.UpToDate,(await UpdateService.CheckAsync(http,new(1,1,0))).State);
    }

    [Theory]
    [InlineData(404,UpdateCheckState.NotPublished)]
    [InlineData(503,UpdateCheckState.Failed)]
    public async Task FailedOrMissingRelease_IsNotReportedAsUpToDate(int status,UpdateCheckState expected)
    {
        using var http=new HttpClient(new ReplyHandler((HttpStatusCode)status,"{}"));
        Assert.Equal(expected,(await UpdateService.CheckAsync(http,new(1,0,0))).State);
    }

    [Theory]
    [InlineData("sha256:bad",null)]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","https://other.invalid/setup.exe")]
    public async Task InvalidDigestOrOrigin_IsRejected(string digest,string? url)
    {
        using var http=new HttpClient(new ReplyHandler(HttpStatusCode.OK,Release("1.1.0",digest,url)));
        UpdateCheckResult result=await UpdateService.CheckAsync(http,new(1,0,0));
        Assert.Equal(UpdateCheckState.Failed,result.State);
        Assert.Null(result.Info);
    }

    [Fact]
    public void InstallerReceivesOldProcessIdAndDoesNotRestartWindows()
    {
        string[] args=UpdateService.InstallerArguments(1234);
        Assert.Contains("/UPDATEPID=1234",args);
        Assert.Contains("/NORESTART",args);
        Assert.Contains("/SILENT",args);
    }
}