using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace Appostolic.Api.Tests.Api;

public class UserAvatarEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public UserAvatarEndpointsTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadAvatar_Succeeds_WithPngUnder2MB()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // RDH Story 2: migrated from dev headers to JWT bearer token
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(client, "kevin@example.com", "kevin-personal");

        // Minimal valid 1x1 PNG bytes (generated via ImageSharp to avoid CRC issues)
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAAC0lEQVR4nGNhAAIAABkABaSlNawAAAAASUVORK5CYII=");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, name: "file", fileName: "avatar.png");

        var res = await client.PostAsync("/api/users/me/avatar", content);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var url = json.GetProperty("avatar").GetProperty("url").GetString();
        url.Should().NotBeNullOrEmpty();
        // We now preserve original format; mime should echo input PNG
        json.GetProperty("avatar").GetProperty("mime").GetString().Should().Be("image/png");
        json.GetProperty("avatar").GetProperty("width").GetInt32().Should().Be(1);
        json.GetProperty("avatar").GetProperty("height").GetInt32().Should().Be(1);
        // API now returns absolute URL for /media to ensure browser can fetch in dev
        url!.Should().StartWith("http");
    }

    [Fact]
    public async Task UploadAvatar_Rejects_UnsupportedMediaType()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // RDH Story 2: migrated from dev headers to JWT bearer token
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(client, "kevin@example.com", "kevin-personal");

        var bytes = Encoding.UTF8.GetBytes("not-an-image");
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, name: "file", fileName: "readme.txt");

        var res = await client.PostAsync("/api/users/me/avatar", content);
        res.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task UploadAvatar_Rejects_TooLarge()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // RDH Story 2: migrated from dev headers to JWT bearer token
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(client, "kevin@example.com", "kevin-personal");

        // Create >2MB dummy PNG bytes (not a valid image, but size check happens first)
        var bytes = new byte[2 * 1024 * 1024 + 1];
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, name: "file", fileName: "big.png");

        var res = await client.PostAsync("/api/users/me/avatar", content);
        res.StatusCode.Should().Be((HttpStatusCode)413); // Payload Too Large
    }

    [Fact]
    public async Task UploadAvatar_Rejects_TooRectangular()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // RDH Story 2: migrated from dev headers to JWT bearer token
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(client, "kevin@example.com", "kevin-personal");

        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2000, 800);
        using var ms = new MemoryStream();
    await img.SaveAsync(ms, new PngEncoder());
        var bytes = ms.ToArray();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, name: "file", fileName: "wide.png");

        var res = await client.PostAsync("/api/users/me/avatar", content);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UploadAvatar_Downscales_LargeImage_To512Webp()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // RDH Story 2: migrated from dev headers to JWT bearer token
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(client, "kevin@example.com", "kevin-personal");

        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1024, 1024);
        using var ms = new MemoryStream();
    await img.SaveAsync(ms, new PngEncoder());
        var bytes = ms.ToArray();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, name: "file", fileName: "big.png");

        var res = await client.PostAsync("/api/users/me/avatar", content);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await res.Content.ReadFromJsonAsync<JsonElement>();
    json.GetProperty("avatar").GetProperty("mime").GetString().Should().Be("image/png");
        json.GetProperty("avatar").GetProperty("width").GetInt32().Should().BeLessOrEqualTo(512);
        json.GetProperty("avatar").GetProperty("height").GetInt32().Should().BeLessOrEqualTo(512);
    }
    [Fact]
    public async Task UploadAvatar_TransparentLogo_PreservesDimensions_Webp()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // RDH Story 2: migrated from dev headers to JWT bearer token
        await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(client, "kevin@example.com", "kevin-personal");
        // Build a 64x64 transparent PNG with a single red pixel to trigger lossless path
        byte[] bytes;
        using (var img = new Image<Rgba32>(64, 64))
        {
            img.ProcessPixelRows(accessor =>
            {
                var row = accessor.GetRowSpan(10);
                row[10] = new Rgba32(255, 0, 0, 255);
            });
            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            bytes = ms.ToArray();
        }
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, name: "file", fileName: "transparent-logo.png");
        var res = await client.PostAsync("/api/users/me/avatar", content);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
    json.GetProperty("avatar").GetProperty("mime").GetString().Should().Be("image/png");
        json.GetProperty("avatar").GetProperty("width").GetInt32().Should().Be(64);
        json.GetProperty("avatar").GetProperty("height").GetInt32().Should().Be(64);
    var url2 = json.GetProperty("avatar").GetProperty("url").GetString();
    url2.Should().NotBeNullOrEmpty();
    url2!.Should().Contain("/media/users/");
    url2.Should().StartWith("http");
    }
}
