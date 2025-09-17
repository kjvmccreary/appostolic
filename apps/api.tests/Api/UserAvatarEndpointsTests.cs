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
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

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
        json.GetProperty("avatar").GetProperty("url").GetString().Should().NotBeNullOrEmpty();
    json.GetProperty("avatar").GetProperty("mime").GetString().Should().Be("image/webp");
    json.GetProperty("avatar").GetProperty("width").GetInt32().Should().Be(1);
    json.GetProperty("avatar").GetProperty("height").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task UploadAvatar_Rejects_UnsupportedMediaType()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

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
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

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
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

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
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

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
        json.GetProperty("avatar").GetProperty("mime").GetString().Should().Be("image/webp");
        json.GetProperty("avatar").GetProperty("width").GetInt32().Should().BeLessOrEqualTo(512);
        json.GetProperty("avatar").GetProperty("height").GetInt32().Should().BeLessOrEqualTo(512);
    }
}
