using NetBooster.Logic;

namespace NetBooster.Services;

public sealed class BufferbloatService
{
    public const string DefaultLoadUrl = "https://speed.cloudflare.com/__down?bytes=25000000";
    private readonly NetworkDiagnostics _diagnostics;
    private readonly AppLogger _log;

    public BufferbloatService(NetworkDiagnostics diagnostics, AppLogger log) { _diagnostics = diagnostics; _log = log; }

    public async Task<BufferbloatResult> MeasureAsync(string downloadUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("URL tạo tải phải là URL HTTPS hợp lệ.");

        var gateway = NetworkDiagnostics.GetDefaultGateway()?.ToString();
        var baselineGateway = gateway is null ? null : _diagnostics.PingAsync(gateway, 8, spacingMs: 200);
        var baselineInternet = _diagnostics.PingAsync("1.1.1.1", 8, spacingMs: 200);
        await Task.WhenAll(baselineInternet, baselineGateway ?? Task.CompletedTask);

        var download = DownloadForAsync(uri, TimeSpan.FromSeconds(7), cancellationToken);
        await Task.Delay(250, cancellationToken);
        var loadedGateway = gateway is null ? null : _diagnostics.PingAsync(gateway, 10, spacingMs: 250);
        var loadedInternet = _diagnostics.PingAsync("1.1.1.1", 10, spacingMs: 250);
        await Task.WhenAll(loadedInternet, loadedGateway ?? Task.CompletedTask);
        var load = await download;

        var internetComparison = BufferbloatLogic.Compare(await baselineInternet, await loadedInternet);
        BufferbloatEndpointResult? gatewayComparison = baselineGateway is null || loadedGateway is null ? null : BufferbloatLogic.Compare(await baselineGateway, await loadedGateway);
        var conclusion = BufferbloatLogic.Conclude(gatewayComparison, internetComparison, load.Error);
        _log.Info("Đã hoàn tất đo bufferbloat/độ ổn định.");
        return new BufferbloatResult(gatewayComparison, internetComparison, load.Bytes, load.Error, conclusion);
    }

    private static async Task<(long Bytes, string? Error)> DownloadForAsync(Uri uri, TimeSpan duration, CancellationToken externalToken)
    {
        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        timeLimit.CancelAfter(duration);
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        long total = 0;
        try
        {
            while (!timeLimit.IsCancellationRequested)
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeLimit.Token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeLimit.Token);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(), timeLimit.Token)) > 0) total += read;
            }
        }
        catch (OperationCanceledException) when (timeLimit.IsCancellationRequested && total > 0)
        {
            return (total, null); // Expected bounded end after a load was generated.
        }
        catch (OperationCanceledException) when (externalToken.IsCancellationRequested) { return (total, "Người dùng đã hủy phép đo."); }
        catch (Exception ex) { return (total, $"Tải tạo tải lỗi: {ex.Message}"); }
        return total > 0 ? (total, null) : (0, "Nguồn tải không trả dữ liệu.");
    }
}
