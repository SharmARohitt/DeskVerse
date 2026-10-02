namespace Deskverse.Security.Network;

using System.Net.Http.Headers;
using System.Security.Cryptography;
using Deskverse.Core;
using Deskverse.Core.Models;
using Deskverse.Security.FileValidation;
using Deskverse.Security.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed record DownloadedFile(string TempPath, long SizeBytes, string FileHash);

/// <summary>
/// Hardened download pipeline: URL policy, response size caps, streaming hash,
/// temporary staging, and atomic promotion. Interrupted downloads never leave
/// partial files that could be mistaken for complete media.
/// </summary>
public sealed class SecureDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly UrlPolicy _urlPolicy;
    private readonly SecurityOptions _options;
    private readonly ILogger<SecureDownloadService> _logger;

    public SecureDownloadService(
        HttpClient httpClient,
        UrlPolicy urlPolicy,
        IOptions<SecurityOptions> options,
        ILogger<SecureDownloadService> logger)
    {
        _httpClient = httpClient;
        _urlPolicy = urlPolicy;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Downloads a remote file into <paramref name="tempRoot"/>. The result is a
    /// fully-validated staged file; the caller promotes it into the cache.
    /// </summary>
    public async Task<OperationResult<DownloadedFile>> DownloadAsync(
        Uri url,
        string tempRoot,
        string expectedExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var policy = _urlPolicy.Validate(url);
        if (!policy.Allowed)
        {
            return OperationResult<DownloadedFile>.Fail($"Blocked URL: {policy.Reason}");
        }

        Directory.CreateDirectory(tempRoot);
        var tempPath = Path.Combine(tempRoot, SecureNames.GenerateTempName(expectedExtension));

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.DownloadTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("DeskVerse/0.1 (+local-wallpaper-engine)");

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<DownloadedFile>.Fail(
                    $"Provider returned {(int)response.StatusCode} {response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > _options.MaxDownloadBytes)
            {
                return OperationResult<DownloadedFile>.Fail(
                    $"Declared size {declared:N0} bytes exceeds the download limit of {_options.MaxDownloadBytes:N0} bytes.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (contentType is not null && !IsAllowedContentType(contentType))
            {
                return OperationResult<DownloadedFile>.Fail($"Content type '{contentType}' is not accepted media.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);

            using var sha = SHA256.Create();
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), timeoutCts.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > _options.MaxDownloadBytes)
                {
                    await target.DisposeAsync().ConfigureAwait(false);
                    TryDelete(tempPath);
                    return OperationResult<DownloadedFile>.Fail(
                        $"Download exceeded the limit of {_options.MaxDownloadBytes:N0} bytes and was aborted.");
                }

                sha.TransformBlock(buffer, 0, read, buffer, 0);
                await target.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token).ConfigureAwait(false);
            }

            sha.TransformFinalBlock([], 0, 0);
            var hash = Convert.ToHexString(sha.Hash!);

            if (total == 0)
            {
                TryDelete(tempPath);
                return OperationResult<DownloadedFile>.Fail("Download returned an empty body.");
            }

            // Final signature check on the staged file; extensions and content types
            // are advisory, bytes are authoritative.
            await using var verifyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true);
            var detected = await FileSignatureValidator.DetectFormatAsync(verifyStream, cancellationToken).ConfigureAwait(false);
            if (detected == WallpaperFormat.Unknown)
            {
                TryDelete(tempPath);
                return OperationResult<DownloadedFile>.Fail("Downloaded content is not a supported media format.");
            }

            _logger.LogInformation("Downloaded {Bytes:N0} bytes from {Host}", total, url.Host);
            return OperationResult<DownloadedFile>.Ok(new DownloadedFile(tempPath, total, hash));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(tempPath);
            return OperationResult<DownloadedFile>.Fail("Download was cancelled.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            return OperationResult<DownloadedFile>.Fail($"Download timed out after {_options.DownloadTimeout.TotalSeconds:N0}s.");
        }
        catch (HttpRequestException ex)
        {
            TryDelete(tempPath);
            return OperationResult<DownloadedFile>.Fail($"Network error during download: {ex.Message}");
        }
        catch (IOException ex)
        {
            TryDelete(tempPath);
            return OperationResult<DownloadedFile>.Fail($"Disk error during download: {ex.Message}");
        }
    }

    private static bool IsAllowedContentType(string contentType) =>
        contentType.StartsWith("image/", StringComparison.Ordinal)
        || contentType.StartsWith("video/", StringComparison.Ordinal)
        || contentType is "application/octet-stream" or "binary/octet-stream";

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not remove partial download {File}", Path.GetFileName(path));
        }
    }
}
