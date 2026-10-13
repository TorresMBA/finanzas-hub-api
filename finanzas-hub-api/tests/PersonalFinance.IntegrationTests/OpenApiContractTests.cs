using System.Text.Json;
using System.Text.Json.Nodes;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>
/// The contract with the frontend is the committed <c>docs/api/openapi.v1.json</c>; the TypeScript client is
/// generated from it. Changing the API without updating that file fails here. To update it on purpose, run
/// <c>tools/update-openapi.ps1</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public class OpenApiContractTests(ApiFixture api)
{
    private const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task The_generated_document_matches_the_committed_contract()
    {
        var generated = JsonNode.Parse(await api.AnonymousClient().GetStringAsync("/openapi/v1.json"))!.AsObject();
        generated.Remove("servers"); // Depends on the host the document was requested from.
        var actual = generated.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(FindRepositoryRoot(), "docs", "api", "openapi.v1.json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"{path} does not exist. Run tools/update-openapi.ps1 to create it.");
        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        Assert.True(expected == actual, "The API no longer matches docs/api/openapi.v1.json. If the change is intended, run tools/update-openapi.ps1 and regenerate the frontend client.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root (.git) was not found above the test output directory.");
    }
}
