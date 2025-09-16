using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Appostolic.Api.Tests.Api;

public class UserProfileEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public UserProfileEndpointsTests(WebAppFactory factory) => _factory = factory;

    private static HttpClient Client(WebAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        c.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task Get_me_returns_user_with_profile()
    {
        var client = Client(_factory);
        var resp = await client.GetAsync("/api/users/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
        json.GetProperty("email").GetString().Should().Be("kevin@example.com");
        // Profile may be null initially
        json.TryGetProperty("profile", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Put_me_merges_profile_and_trims_and_validates_social_urls()
    {
        var client = Client(_factory);

        // Seed profile with nested values
        var seed = new
        {
            name = new { first = " Kevin ", last = " McCreary" },
            social = new { twitter = " http://twitter.com/kevin ", github = "not-a-url" },
            preferences = new { theme = "dark", languages = new[] { "en", "es" } },
        };
        var seedResp = await client.PutAsJsonAsync("/api/users/me", seed);
        seedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var seeded = await seedResp.Content.ReadFromJsonAsync<JsonElement>();

        // Patch: update nested first name, replace array, null out a field, and add new field
        var patch = new
        {
            name = new { first = "Kevin", middle = "A" },
            preferences = new { languages = new[] { "en" } },
            social = new { github = "https://github.com/km" },
            bio = "  Pastor and builder  ",
            obsolete = (string?)null
        };
        var patchResp = await client.PutAsJsonAsync("/api/users/me", patch);
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patchResp.Content.ReadFromJsonAsync<JsonElement>();

        // Validate trim + merge
        var profile = updated!.GetProperty("profile");
        profile.GetProperty("name").GetProperty("first").GetString().Should().Be("Kevin");
        profile.GetProperty("name").GetProperty("last").GetString().Should().Be("McCreary");
        profile.GetProperty("name").GetProperty("middle").GetString().Should().Be("A");

        // Arrays replace entirely
        var langs = profile.GetProperty("preferences").GetProperty("languages").EnumerateArray().Select(e => e.GetString()).ToArray();
        langs.Should().BeEquivalentTo(new[] { "en" });

        // Social: invalid url dropped, http(s) kept and trimmed
        var social = profile.GetProperty("social");
        social.TryGetProperty("twitter", out var twitter).Should().BeTrue();
        twitter.GetString().Should().Be("http://twitter.com/kevin");
        social.TryGetProperty("github", out var github).Should().BeTrue();
        github.GetString().Should().Be("https://github.com/km");

        // New field added and trimmed
        profile.GetProperty("bio").GetString().Should().Be("Pastor and builder");

        // Explicit null allowed
        profile.TryGetProperty("obsolete", out var obsolete).Should().BeTrue();
        obsolete.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_me_rejects_non_object_body()
    {
        var client = Client(_factory);
        var content = new StringContent("\"not-an-object\"", System.Text.Encoding.UTF8, "application/json");
        var resp = await client.PutAsync("/api/users/me", content);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
