using System.Net.Http.Json;
using Appostolic.Api.Tests;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Appostolic.Api.AuthTests;

public class LoginTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public LoginTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401()
    {
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = "nouser@example.com", password = "nope" });
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200()
    {
        using var client = _factory.CreateClient();

        // 1) Sign up a new user (self-serve path)
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email = "loginuser@example.com", password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // 2) Attempt login with same credentials
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "loginuser@example.com", password = "Password123!" });
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        payload.Should().NotBeNull();
        payload!.Email.Should().Be("loginuser@example.com");
        payload.Id.Should().NotBe(Guid.Empty);
    }

    private sealed record LoginResponse(Guid Id, string Email);
}
