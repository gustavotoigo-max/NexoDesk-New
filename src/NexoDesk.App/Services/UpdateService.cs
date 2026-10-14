using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using NexoDesk.App.ViewModels;
using NexoDesk.Desktop;

namespace NexoDesk.App.Services;

public enum UpdateCheckState { UpToDate, Available, NotPublished, Failed }
public sealed record UpdateCheckResult(UpdateCheckState State, UpdateInfo? Info, string Message);

public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string Notes, string Sha256);

/// <summary>Verifica o SHA-256 do asset de Releases antes de executar o instalador.</summary>
public sealed class UpdateService
{
    private const string Repository = "gustavotoigo-max/NexoDesk-New";
    private static readonly HttpClient Http = CreateClient();
    private static readonly HttpClient Probe = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ReleasePoller Poller = new(Probe, Current, ct => CheckAsync(ct));

    public static Task<UpdateCheckResult> PollAsync(CancellationToken ct = default) => Poller.CheckAsync(ct);
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NexoDesk-New-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default) => CheckAsync(Http, Current, ct);

    public static async Task<UpdateCheckResult> CheckAsync(HttpClient http, Version current, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(UpdateCheckState.NotPublished, null, "Ainda não há versão publicada no GitHub.");
            response.EnsureSuccessStatusCode();
            GitHubRelease? release = await response.Content.ReadFromJsonAsync(UpdateJson.Default.GitHubRelease, timeout.Token).ConfigureAwait(false);
            if (release?.TagName is null || release.Draft || release.Prerelease ||
                !Version.TryParse(release.TagName.TrimStart('v', 'V'), out Version? latest))
                return new(UpdateCheckState.Failed, null, "A publicação não contém uma versão válida.");
            if (latest == current) return new(UpdateCheckState.UpToDate, null, $"Versão {current} atualizada.");
            if (latest < current) return new(UpdateCheckState.UpToDate, null,
                $"Versão instalada: {current}. Último release publicado: {latest}.");
            GitHubAsset? setup = release.Assets?.FirstOrDefault(a =>
                a.Name == $"NexoDesk-New-Setup-{latest}.exe" && a.Digest?.StartsWith("sha256:", StringComparison.Ordinal) == true);
            if (setup?.BrowserDownloadUrl is null || setup.Digest is null)
                return new(UpdateCheckState.Failed, null, "A versão publicada está sem instalador verificável.");
            string digest = setup.Digest["sha256:".Length..];
            if (digest.Length != 64 || !digest.All(Uri.IsHexDigit) ||
                !setup.BrowserDownloadUrl.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal))
                return new(UpdateCheckState.Failed, null, "Origem ou SHA-256 da publicação inválidos.");
            var info = new UpdateInfo(latest, release.TagName, setup.BrowserDownloadUrl, release.Body ?? "", digest);
            return new(UpdateCheckState.Available, info, $"Atualização disponível: versão {latest}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        { return new(UpdateCheckState.Failed, null, "Não foi possível consultar o GitHub. Verifique a internet e tente novamente."); }
    }
    public static async Task<bool> DownloadAndRunAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!info.DownloadUrl.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal) ||
            info.Sha256.Length != 64 || !info.Sha256.All(Uri.IsHexDigit)) return false;
        string directory = Path.Combine(MachineState.DataDirectory, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"NexoDesk-New-Setup-{info.Version}.exe");
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            const long limit = 256L * 1024 * 1024;
            if (total > limit) return false;
            await using (Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    read += n;
                    if (read > limit) return false;
                    await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    if (total is > 0) progress?.Report((double)read / total.Value);
                }
            }
            await using var verify = File.OpenRead(path);
            byte[] actual = await SHA256.HashDataAsync(verify, ct).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(info.Sha256))) return false;
            verify.Close();
            var start = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" };
            foreach (string argument in InstallerArguments(Environment.ProcessId)) start.ArgumentList.Add(argument);
            using Process? installer = Process.Start(start);
            if (installer is null) return false;
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or System.ComponentModel.Win32Exception) { return false; }
    }

    public static string[] InstallerArguments(int oldProcessId)
    {
        if (oldProcessId <= 0) throw new ArgumentOutOfRangeException(nameof(oldProcessId));
        return ["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", $"/UPDATEPID={oldProcessId}"];
    }

    private static Version Current => Version.TryParse(AppInfo.Version, out Version? v) ? v : new(0, 0, 0);
}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);
internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
    [property: JsonPropertyName("digest")] string? Digest);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJson : JsonSerializerContext;