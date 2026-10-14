using NetArchTest.Rules;

namespace PersonalFinance.ArchitectureTests;

/// <summary>
/// Boundaries between projects and between modules. A module is one project whose types are public, so nothing
/// but these tests stops one module from reaching into another: when one fails, the code is fixed, not the test.
/// </summary>
public class ModuleBoundaryTests
{
    /// <summary>
    /// The module graph of the architecture (docs/architecture/module-communication.md). A module may use the
    /// Contracts of the modules listed here and of no other. Adding a module means adding its entry.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedDependencies = new(StringComparer.Ordinal)
    {
        ["Identity"] = [],
        ["Workspaces"] = ["Identity"],
        ["Categories"] = ["Workspaces"],
        ["Notifications"] = ["Identity"],
        ["Ledger"] = ["Categories", "Notifications"],
        ["Budgets"] = ["Ledger", "Categories", "Notifications"],
        ["Scheduling"] = ["Ledger", "Categories", "Notifications", "Workspaces"],
        ["Goals"] = ["Ledger", "Workspaces", "Notifications"],
        ["SharedExpenses"] = ["Ledger", "Workspaces", "Notifications"],
        ["Reporting"] = ["Ledger", "Categories", "Budgets", "Scheduling", "Goals"]
    };

    [Fact]
    public void The_rules_have_something_to_check()
    {
        // Guards against every other test passing because discovery silently found nothing.
        Assert.NotEmpty(Solution.Modules);
        Assert.All(
            new[] { Solution.Api, Solution.Worker, Solution.Infrastructure, Solution.BuildingBlocksDomain, Solution.BuildingBlocksApplication },
            name => Assert.Contains(Solution.Assemblies, assembly => Solution.NameOf(assembly) == name));
        Assert.All(Solution.Modules, module => Assert.Contains(Solution.Projects, project => project.Name == Solution.NameOf(module)));
        Assert.NotEmpty(Solution.ControllersByFolder);
        Assert.NotEmpty(Solution.JobsByFolder);
    }

    [Fact]
    public void Every_module_is_declared_in_the_dependency_graph()
    {
        Assert.DoesNotContain(Solution.ModuleNames, module => !AllowedDependencies.ContainsKey(module));
    }

    [Fact]
    public void A_module_project_references_only_building_blocks_and_the_modules_the_graph_allows()
    {
        var violations = new List<string>();
        foreach (var project in Solution.Projects.Where(project => Solution.IsModule(project.Name)))
        {
            var module = Solution.ModuleNameOf(project.Name);
            foreach (var reference in project.ProjectReferences.Where(reference => !Solution.IsBuildingBlock(reference)))
            {
                if (!Solution.IsModule(reference))
                {
                    violations.Add($"{project.Name} references {reference}; a module may reference only building blocks and other modules.");
                }
                else if (!AllowedDependencies.GetValueOrDefault(module, []).Contains(Solution.ModuleNameOf(reference)))
                {
                    violations.Add($"{module} depends on {Solution.ModuleNameOf(reference)}, which the module graph does not allow.");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void A_module_project_brings_no_database_provider_and_no_web_framework()
    {
        // EF Core's abstractions are the one package a module needs: its use cases query through DbSet.
        // Identity also uses ASP.NET Core Identity's library part (user type, password hashing, tokens), which has no web dependency.
        string[] allowedPackages = ["Microsoft.EntityFrameworkCore"];
        var allowedPerModule = new Dictionary<string, string[]> { ["Identity"] = ["Microsoft.Extensions.Identity.Stores"] };

        var violations = Solution.Projects.Where(project => Solution.IsModule(project.Name))
            .SelectMany(project => project.PackageReferences
                .Where(package => !allowedPackages.Contains(package)
                    && !allowedPerModule.GetValueOrDefault(Solution.ModuleNameOf(project.Name), []).Contains(package))
                .Concat(project.FrameworkReferences)
                .Select(reference => $"{project.Name} references {reference}."));

        Assert.Empty(violations);
    }

    [Fact]
    public void A_module_uses_only_the_contracts_of_another_module()
    {
        var violations = new List<string>();
        foreach (var module in Solution.Modules)
        {
            var name = Solution.ModuleNameOf(Solution.NameOf(module));
            var internalsOfOthers = Solution.ModuleNames.Where(other => other != name)
                .SelectMany(other => new[] { $"{Solution.ModuleRoot(other)}.Domain", $"{Solution.ModuleRoot(other)}.Application" })
                .ToArray();
            if (internalsOfOthers.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(module).ShouldNot().HaveDependencyOnAny(internalsOfOthers).GetResult();
            violations.AddRange(result.FailingTypeNames ?? []);
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Building_blocks_never_reference_a_module_infrastructure_or_a_host()
    {
        var violations = Solution.Projects.Where(project => Solution.IsBuildingBlock(project.Name))
            .SelectMany(project => project.ProjectReferences.Where(reference => !Solution.IsBuildingBlock(reference))
                .Select(reference => $"{project.Name} references {reference}."));

        Assert.Empty(violations);
    }

    [Fact]
    public void The_domain_building_block_has_no_dependencies_and_the_application_one_only_the_domain()
    {
        var domain = Solution.Projects.Single(project => project.Name == Solution.BuildingBlocksDomain);
        var application = Solution.Projects.Single(project => project.Name == Solution.BuildingBlocksApplication);

        Assert.Empty(domain.ProjectReferences.Concat(domain.PackageReferences).Concat(domain.FrameworkReferences));
        Assert.Equal([Solution.BuildingBlocksDomain], application.ProjectReferences);
        Assert.Empty(application.PackageReferences.Concat(application.FrameworkReferences));
    }

    [Fact]
    public void Infrastructure_is_referenced_only_by_the_hosts_and_the_hosts_do_not_reference_each_other()
    {
        string[] hosts = [Solution.Api, Solution.Worker];

        var violations = Solution.Projects
            .SelectMany(project => project.ProjectReferences, (project, reference) => (Project: project.Name, Reference: reference))
            .Where(pair => (pair.Reference == Solution.Infrastructure && !hosts.Contains(pair.Project)) || hosts.Contains(pair.Reference))
            .Select(pair => $"{pair.Project} references {pair.Reference}.");

        Assert.Empty(violations);
    }
}
