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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class UserAvatarEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public UserAvatarEndpointsTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    // RDH Story 2 Phase A: migrate from mint/dev helper to real password -> login -> select-tenant auth flow
    private const string DefaultPw = "Password123!"; // must match AuthTestClientFlow.DefaultPassword

    private async Task SeedPasswordAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var (hash, salt, _) = hasher.HashPassword(password);
        db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UploadAvatar_Succeeds_WithPngUnder2MB()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");

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
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");

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
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");

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
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");

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
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");

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
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");
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
