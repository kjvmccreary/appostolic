using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Guards against regression: requests supplying legacy development auth headers must be rejected
/// with a 401 once dev header decommission completes (pre-removal phase asserts Unauthorized status; later
/// iterations may also assert structured error code body such as { code: "dev_headers_removed" }).
/// </summary>
/// <summary>
/// Future-stage regression guard (Stories 3-5). Intentionally uses legacy header names to verify they are rejected.
/// Excluded from Phase B migration completion criteria; retained until Physical Removal (Story 4) then adapted
/// to assert the final structured error payload.
/// </summary>
public class DevHeadersRemovedTests
{
    [Fact]
    public async Task DevHeader_Request_Is_Unauthorized()
    {
        var factory = new WebAppFactory()
            .WithSettings(new Dictionary<string,string?>
            {
                ["AUTH__ALLOW_DEV_HEADERS"] = "false"
            });
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/auth-smoke/ping");
        req.Headers.Add("x-dev-user", "kevin@example.com");
        req.Headers.Add("x-tenant", "kevin-personal");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        // TODO (Story 3/4): When deprecation middleware / final removal adds structured body, assert JSON code here.
    }
}
