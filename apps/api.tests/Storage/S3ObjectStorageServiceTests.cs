using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Appostolic.Api.Application.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Appostolic.Api.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="S3ObjectStorageService"/> validating PutObject request shape and returned URL.
/// </summary>
public class S3ObjectStorageServiceTests
{
    [Fact]
    public async Task UploadAsync_UploadsWithPublicReadAndCacheControl_ReturnsPublicBaseUrl()
    {
        // Arrange
    var mockS3 = new Mock<IAmazonS3>(MockBehavior.Loose); // Loose to allow Dispose without explicit setup
        PutObjectRequest? capturedRequest = null;
        mockS3
            .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK, ETag = "\"abc123\"" });

        var opts = Options.Create(new S3StorageOptions
        {
            Bucket = "test-bucket",
            PublicBaseUrl = "https://cdn.example.com",
            DefaultCacheControl = "public, max-age=42",
            PathStyle = true,
            RegionEndpoint = "us-east-1"
        });

        using var service = new S3ObjectStorageService(mockS3.Object, opts, new Tests.TestUtilities.FakeLogger<S3ObjectStorageService>());

        var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        // Act
        var (url, key) = await service.UploadAsync("users/123/avatar.png", "image/png", content, CancellationToken.None);

        // Assert
        key.Should().Be("users/123/avatar.png");
        url.Should().Be("https://cdn.example.com/users/123/avatar.png");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.BucketName.Should().Be("test-bucket");
        capturedRequest.Key.Should().Be("users/123/avatar.png");
        capturedRequest.ContentType.Should().Be("image/png");
        capturedRequest.CannedACL.Should().Be(S3CannedACL.PublicRead);
        capturedRequest.Headers.CacheControl.Should().Be("public, max-age=42");

        mockS3.VerifyAll();
    }

    [Fact]
    public async Task UploadAsync_FallbacksToBucketRegionUrl_WhenNoPublicBaseUrl()
    {
        // Arrange
    var mockS3 = new Mock<IAmazonS3>(MockBehavior.Loose); // Loose to allow Dispose without explicit setup
        mockS3
            .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK, ETag = "\"etag\"" });

        var opts = Options.Create(new S3StorageOptions
        {
            Bucket = "fallback-bucket",
            RegionEndpoint = "us-west-2",
            PathStyle = false
        });

        using var service = new S3ObjectStorageService(mockS3.Object, opts, new Tests.TestUtilities.FakeLogger<S3ObjectStorageService>());
        var content = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var (url, _) = await service.UploadAsync("users/abc/avatar.webp", "image/webp", content);

        // Assert
        url.Should().Be("https://fallback-bucket.s3.us-west-2.amazonaws.com/users/abc/avatar.webp");
    }
}
