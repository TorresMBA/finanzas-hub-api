using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>What every endpoint gets from the platform: health, errors as problem details, correlation and the three access levels.</summary>
[Collection(ApiCollection.Name)]
public class HttpPipelineTests(ApiFixture api)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer_without_a_session(string path)
    {
        var response = await api.AnonymousClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_session_gets_401_as_a_problem_with_code_and_trace_id()
    {
        var response = await api.AnonymousClient().GetAsync(Books.AccountsUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("auth.unauthenticated", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_cannot_tell_the_workspace_exists()
    {
        var (_, workspaceId) = api.NewMember();
        var outsider = api.ClientFor(Guid.NewGuid());

        var response = await outsider.GetAsync(Books.AccountsUrl(workspaceId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("workspace.not_found", (await ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_body_that_breaks_the_shape_rules_gets_400_with_messages_per_field()
    {
        var (userId, workspaceId) = api.NewMember();

        var response = await api.ClientFor(userId).PostWithKeyAsync(Books.AccountsUrl(workspaceId), new { name = "", type = "bank", currency = "PEN" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("name", out _));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Malformed_json_gets_400_instead_of_500()
    {
        var (userId, workspaceId) = api.NewMember();
        var request = new HttpRequestMessage(HttpMethod.Post, Books.AccountsUrl(workspaceId))
        {
            Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json")
        };

        var response = await api.ClientFor(userId).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_command_with_an_effect_is_refused_without_an_idempotency_key()
    {
        var (userId, workspaceId) = api.NewMember();

        var response = await api.ClientFor(userId).PostAsJsonAsync(Books.AccountsUrl(workspaceId), new { name = "Sin clave", type = "bank", currency = "PEN" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency.key_required", (await ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_correlation_id_sent_by_the_caller_is_echoed_and_an_unsafe_one_is_replaced()
    {
        var client = api.AnonymousClient();

        var accepted = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        accepted.Headers.Add("X-Correlation-Id", "web-7f3a_01");
        var rejected = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        rejected.Headers.Add("X-Correlation-Id", "bad value;drop");

        var first = await client.SendAsync(accepted);
        var second = await client.SendAsync(rejected);

        Assert.Equal("web-7f3a_01", first.Headers.GetValues("X-Correlation-Id").Single());
        var replaced = second.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual("bad value;drop", replaced);
        Assert.NotEmpty(replaced);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        var response = await api.AnonymousClient().GetAsync("/health/live");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
