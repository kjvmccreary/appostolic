using Microsoft.Extensions.Options;

namespace Appostolic.Api.Application.Storage;

/// <summary>
/// Development/Test implementation that writes files to a local folder and exposes them via /media URLs.
/// </summary>
public sealed class LocalFileStorageService : IObjectStorageService
{
    private readonly LocalFileStorageOptions _options;

    public LocalFileStorageService(IOptions<LocalFileStorageOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.BasePath))
        {
            // Default to web public folder under repo for simplicity
            _options.BasePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "web.out", "media");
        }
        Directory.CreateDirectory(_options.BasePath);
    }

    public async Task<(string Url, string Key)> UploadAsync(string key, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        // Normalize key and ensure directories exist
        key = key.Replace('\\', '/');
        var fullPath = Path.Combine(_options.BasePath, key);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using (var fs = File.Create(fullPath))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }

        // Build a URL relative to /media
        var urlPath = $"/media/{key.Replace("\\", "/")}";
        return (urlPath, key);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        key = key.Replace('\\', '/');
        var fullPath = Path.Combine(_options.BasePath, key);
        try
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch
        {
            // Swallow for now (idempotent delete). Consider logging at debug level later.
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        key = key.Replace('\\', '/');
        var fullPath = Path.Combine(_options.BasePath, key);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }
}

public sealed class LocalFileStorageOptions
{
    /// <summary>
    /// Absolute filesystem path where objects should be stored.
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}
