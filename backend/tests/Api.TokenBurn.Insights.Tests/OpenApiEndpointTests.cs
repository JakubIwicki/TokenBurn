using System.Net;
using System.Text.Json;
using Api.TokenBurn.Insights.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.TokenBurn.Insights.Tests;

public sealed class OpenApiEndpointTests
{
    // The OpenAPI document is generated from endpoint metadata alone, so these
    // tests never open a database connection; the host still demands a
    // ConnectionStrings:Insights value at build time, so a non-resolving one
    // satisfies it.
    private const string UnusedConnectionString = "Host=localhost;Database=tokenburn;Username=insights_role;Password=test";

    [Fact]
    public async Task ReturnsOpenApiDocument_WhenAuthenticated()
    {
        await using WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(UnusedConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("openapi").GetString().Should().NotBeNullOrWhiteSpace();
        IReadOnlyList<string> paths = body.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Select(path => path.Name)
            .ToList();
        paths.Should().Contain("/api/runs");
        paths.Should().Contain("/api/findings");
        paths.Should().Contain("/api/ask");

        JsonElement pathsElement = body.RootElement.GetProperty("paths");
        QueryParameterNames(pathsElement.GetProperty("/api/search")).Should().Contain("Q");
        QueryParameterNames(pathsElement.GetProperty("/api/runs")).Should().Contain(["From", "MinCost"]);
        QueryParameterNames(pathsElement.GetProperty("/api/findings")).Should().Contain("Acknowledged");
        JsonElement askPost = pathsElement.GetProperty("/api/ask").GetProperty("post");
        IReadOnlyList<string> askContentTypes = askPost.GetProperty("requestBody")
            .GetProperty("content")
            .EnumerateObject()
            .Select(entry => entry.Name)
            .ToList();
        askContentTypes.Should().Contain("application/json");
    }

    private static IReadOnlyList<string> QueryParameterNames(JsonElement pathItem)
        => pathItem.GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .Select(parameter => parameter.GetProperty("name").GetString()!)
            .ToList();

    [Fact]
    public async Task RunsEndpoint200_DeclaresResponseSchema()
    {
        await using WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(UnusedConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement runs200Content = body.RootElement.GetProperty("paths")
            .GetProperty("/api/runs")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");
        IReadOnlyList<string> contentTypes = runs200Content
            .EnumerateObject()
            .Select(entry => entry.Name)
            .ToList();
        contentTypes.Should().NotBeEmpty();
        contentTypes.Should().Contain("application/json");
    }

    [Fact]
    public async Task ReturnsUnauthorized_WhenAnonymous()
    {
        await using WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(UnusedConnectionString);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage message = new(HttpMethod.Get, "/openapi/v1.json");
        message.Headers.Add(InsightsTestHost.NoAuthHeader, "true");

        using HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
