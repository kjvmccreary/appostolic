using System.Text;
using Appostolic.Api.Application.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace Appostolic.Api.Tests.Storage;

public class ObjectStorageDeleteTests
{
    [Fact]
    public async Task Local_DeleteAsync_RemovesFile_WhenPresent()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "localstore-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new LocalFileStorageOptions { BasePath = tempDir });
        var service = new LocalFileStorageService(options);
        Directory.CreateDirectory(tempDir);
        var data = Encoding.UTF8.GetBytes("hello");
        await service.UploadAsync("foo/bar.txt", "text/plain", new MemoryStream(data));
        var path = Path.Combine(tempDir, "foo", "bar.txt");
        File.Exists(path).Should().BeTrue();

        // Act
        await service.DeleteAsync("foo/bar.txt");

        // Assert
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task S3_DeleteAsync_CallsDeleteObject()
    {
        // Arrange
        var mock = new Mock<IAmazonS3>(MockBehavior.Loose);
        mock.Setup(m => m.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());
        var opts = Options.Create(new S3StorageOptions { Bucket = "bucket" });
        using var service = new S3ObjectStorageService(mock.Object, opts, new Tests.TestUtilities.FakeLogger<S3ObjectStorageService>());

        // Act
        await service.DeleteAsync("users/123/avatar.png");

        // Assert
        mock.Verify(m => m.DeleteObjectAsync(It.Is<DeleteObjectRequest>(r => r.BucketName == "bucket" && r.Key == "users/123/avatar.png"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
