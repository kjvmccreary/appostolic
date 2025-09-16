namespace Appostolic.Api.Application.Storage;

/// <summary>
/// Abstraction for object storage for user-uploaded assets (avatars/logos).
/// Implementations may target local filesystem, MinIO, or S3-compatible stores.
/// </summary>
public interface IObjectStorageService
{
    /// <summary>
    /// Uploads an object stream to the backing store at the given key and returns a public URL.
    /// Implementations may overwrite any existing object at the same key.
    /// </summary>
    /// <param name="key">Path-like key (e.g., users/{userId}/avatar.png)</param>
    /// <param name="contentType">Mime type (e.g., image/png)</param>
    /// <param name="content">Readable stream; method will not dispose it.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (Url, Key)</returns>
    Task<(string Url, string Key)> UploadAsync(string key, string contentType, Stream content, CancellationToken cancellationToken = default);
}
