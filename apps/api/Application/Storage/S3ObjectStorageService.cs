using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Application.Storage;

/// <summary>
/// S3 / MinIO implementation of <see cref="IObjectStorageService"/> supporting public object URLs.
/// Designed for dev MinIO (path-style) and production S3 (virtual-host-style) based on options.
/// </summary>
public sealed class S3ObjectStorageService : IObjectStorageService, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3ObjectStorageService> _logger;
    private bool _disposed;

    public S3ObjectStorageService(IAmazonS3 s3, IOptions<S3StorageOptions> options, ILogger<S3ObjectStorageService> logger)
    {
        _s3 = s3;
        _options = options.Value;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(_options.Bucket))
        {
            throw new InvalidOperationException("S3 storage bucket not configured (Storage:S3:Bucket)");
        }
    }

    /// <inheritdoc />
    public async Task<(string Url, string Key)> UploadAsync(string key, string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key required", nameof(key));
        key = key.Replace('\\', '/');

        var put = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = contentType,
            CannedACL = S3CannedACL.PublicRead // Public objects (avatars/logos)
        };
        put.Headers.CacheControl = _options.DefaultCacheControl ?? "public, max-age=31536000, immutable";

        var response = await _s3.PutObjectAsync(put, cancellationToken);
        _logger.LogInformation("Uploaded object {Bucket}/{Key} (ETag={ETag}, Status={Status})", _options.Bucket, key, response.ETag, response.HttpStatusCode);

        var publicUrl = BuildPublicUrl(key);
        return (publicUrl, key);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        key = key.Replace('\\', '/');
        try
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Ignore missing object (idempotent)
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete object {Bucket}/{Key}", _options.Bucket, key);
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Replace('\\', '/');

        try
        {
            using var response = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key
            }, cancellationToken);

            var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            return memory;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private string BuildPublicUrl(string key)
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";
        }
        // Fallback: construct based on region + bucket (virtual-host style) — best effort
        var regionHost = _options.RegionEndpoint ?? "us-east-1";
        return $"https://{_options.Bucket}.s3.{regionHost}.amazonaws.com/{key}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _s3.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Options for S3 / MinIO backed object storage.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>Bucket name for object storage.</summary>
    public string Bucket { get; set; } = string.Empty;
    /// <summary>Optional public base URL (e.g., https://cdn.example.com or http://localhost:9000/media). If set, used to form returned object URLs.</summary>
    public string? PublicBaseUrl { get; set; }
    /// <summary>Region endpoint name (e.g., us-east-1) used for fallback URL construction.</summary>
    public string? RegionEndpoint { get; set; }
    /// <summary>Default Cache-Control header applied to uploaded objects.</summary>
    public string? DefaultCacheControl { get; set; }
    /// <summary>When true, configures the AWS SDK for path-style addressing (MinIO/local dev).</summary>
    public bool PathStyle { get; set; } = true;
    /// <summary>AWS access key (MinIO access key in dev).</summary>
    public string? AccessKey { get; set; }
    /// <summary>AWS secret key (MinIO secret key in dev).</summary>
    public string? SecretKey { get; set; }
    /// <summary>Endpoint override for MinIO (e.g., http://localhost:9000).</summary>
    public string? ServiceURL { get; set; }
}