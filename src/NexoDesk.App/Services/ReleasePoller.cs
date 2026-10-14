using System.IO;
using System.Net;
using System.Net.Http;

namespace NexoDesk.App.Services;

/// <summary>HEAD lê só os cabeçalhos do último release; a API completa é consultada apenas para versão nova.</summary>
internal sealed class ReleasePoller(HttpClient http, Version current,
    Func<CancellationToken, Task<UpdateCheckResult>> readMetadata)
{
    private const string Prefix = "https://github.com/gustavotoigo-max/NexoDesk-New/releases/tag/";
    private string? _cachedTag;
    private UpdateCheckResult? _cached;
    private DateTimeOffset _validUntil;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head,
                "https://github.com/gustavotoigo-max/NexoDesk-New/releases/latest");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateCheckState.NotPublished, null, "Ainda não há versão publicada no GitHub.");
            Uri? location = response.Headers.Location;
            if (location is { IsAbsoluteUri: false }) location = new Uri(request.RequestUri!, location);
            string? address = location?.AbsoluteUri;
            if (response.StatusCode is not HttpStatusCode.Found and not HttpStatusCode.MovedPermanently and not HttpStatusCode.SeeOther ||
                address is null || !address.StartsWith(Prefix, StringComparison.Ordinal))
                return new(UpdateCheckState.Failed, null, "Não foi possível identificar o último release. Tente verificar novamente.");
            string tag = Uri.UnescapeDataString(address[Prefix.Length..]);
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest))
                return new(UpdateCheckState.Failed, null, "A publicação não contém uma versão válida.");
            if (latest <= current)
                return new(UpdateCheckState.UpToDate, null, latest == current ? $"Versão {current} atualizada."
                    : $"Versão instalada: {current}. Último release publicado: {latest}.");
            if (tag == _cachedTag && _cached is not null && DateTimeOffset.UtcNow < _validUntil) return _cached;
            UpdateCheckResult result = await readMetadata(timeout.Token).ConfigureAwait(false);
            // Falhas transitórias não ficam presas no cache. Um novo tag invalida o cache imediatamente.
            if (result.State == UpdateCheckState.Available && result.Info?.Tag == tag)
            {
                _cachedTag = tag;
                _cached = result;
                _validUntil = DateTimeOffset.UtcNow.AddMinutes(15);
            }
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        { return new(UpdateCheckState.Failed, null, "Não foi possível consultar o GitHub. Verifique a internet e tente novamente."); }
    }
}
