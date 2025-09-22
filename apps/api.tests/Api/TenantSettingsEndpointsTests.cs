using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Appostolic.Api.AuthTests; // AuthTestClientFlow

namespace Appostolic.Api.Tests.Api;

public class TenantSettingsEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public TenantSettingsEndpointsTests(WebAppFactory factory) => _factory = factory;

    // RDH Story 2 Phase A: migrate from mint helper UseTenantAsync to real password + login + select-tenant flow.
    private async Task<HttpClient> ClientAsync()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, c, "kevin@example.com", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task Get_Settings_Returns_Empty_Object_When_Null()
    {
    var client = await ClientAsync();
        var resp = await client.GetAsync("/api/tenants/settings");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
        json.GetProperty("settings").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Put_Settings_Deep_Merges()
    {
    var client = await ClientAsync();
        var seed = new { branding = new { colors = new { primary = "#123456", secondary = "#abcdef" } } };
        var seedResp = await client.PutAsJsonAsync("/api/tenants/settings", seed);
        seedResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var patch = new { branding = new { colors = new { primary = "#000000" }, tagline = " Faith and Tech " } };
        var patchResp = await client.PutAsJsonAsync("/api/tenants/settings", patch);
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patchResp.Content.ReadFromJsonAsync<JsonElement>();
        var settings = updated.GetProperty("settings");
        settings.GetProperty("branding").GetProperty("colors").GetProperty("primary").GetString().Should().Be("#000000");
        settings.GetProperty("branding").GetProperty("colors").GetProperty("secondary").GetString().Should().Be("#abcdef");
        settings.GetProperty("branding").GetProperty("tagline").GetString().Should().Be(" Faith and Tech ");
    }

    [Fact]
    public async Task Upload_Logo_Succeeds_And_Stores_Metadata()
    {
    var client = await ClientAsync();
        // 1x1 PNG (validated in MinimalPngDecodeTests)
        var pngBytes = Convert.FromBase64String(ValidMinimalPngBase64);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, name: "file", fileName: "logo.png");
        var resp = await client.PostAsync("/api/tenants/logo", content);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("logo").GetProperty("url").GetString().Should().NotBeNullOrEmpty();
        var logo = json.GetProperty("logo");
        logo.GetProperty("mime").GetString().Should().Be("image/webp");
        logo.GetProperty("width").GetInt32().Should().BeGreaterThan(0);
        logo.GetProperty("height").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Upload_Logo_Rejects_Unsupported_Media_Type()
    {
    var client = await ClientAsync();
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes("plain");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, name: "file", fileName: "logo.txt");
        var resp = await client.PostAsync("/api/tenants/logo", content);
        resp.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Upload_Logo_Rejects_Payload_Too_Large()
    {
    var client = await ClientAsync();
        using var content = new MultipartFormDataContent();
        var bytes = new byte[2 * 1024 * 1024 + 10];
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, name: "file", fileName: "big.png");
        var resp = await client.PostAsync("/api/tenants/logo", content);
        ((int)resp.StatusCode).Should().Be(413);
    }

    [Fact]
    public async Task Delete_Logo_Removes_Metadata()
    {
    var client = await ClientAsync();
        // First upload
        var pngBytes = Convert.FromBase64String(ValidMinimalPngBase64);
        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(pngBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, name: "file", fileName: "logo.png");
            var uploadResp = await client.PostAsync("/api/tenants/logo", content);
            uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var delResp = await client.DeleteAsync("/api/tenants/logo");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // GET settings should not contain logo metadata
        var getResp = await client.GetAsync("/api/tenants/settings");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var settings = json.GetProperty("settings");
        if (settings.TryGetProperty("branding", out var branding))
        {
            branding.TryGetProperty("logo", out var _).Should().BeFalse();
        }
    }

    private const string ValidMinimalPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAAC0lEQVR4nGNhAAIAABkABaSlNawAAAAASUVORK5CYII="; // 1x1 PNG
}
