using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace PersonalFinance.ArchitectureTests;

/// <summary>
/// Clean Architecture inside each module, and thin entry points around them. Layers are namespaces of one
/// project, so these tests are what keeps them apart.
/// </summary>
public class LayeringTests
{
    private const string EfCore = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";

    /// <summary>Job folders that do not belong to a module: housekeeping of the platform's own tables.</summary>
    private static readonly string[] PlatformJobFolders = ["Platform"];

    /// <summary>Types allowed to bypass the ownership filters. Only system code that acts for every user belongs here.</summary>
    private static readonly string[] MayIgnoreQueryFilters = [];

    [Fact]
    public void Domain_depends_only_on_the_domain_building_block_and_its_own_contracts()
    {
        AssertNoDependencies("Domain", root =>
            [EfCore, AspNetCore, Npgsql, Solution.BuildingBlocksApplication, Solution.Infrastructure, $"{root}.Application"]);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_http()
    {
        // EF Core's abstractions (DbSet, IQueryable extensions) are allowed on purpose; see ISampleDb.
        AssertNoDependencies("Application", _ => [AspNetCore, Npgsql, Solution.Infrastructure]);
    }

    [Fact]
    public void Contracts_depend_only_on_the_domain_building_block()
    {
        AssertNoDependencies("Contracts", root =>
            [EfCore, AspNetCore, Npgsql, Solution.BuildingBlocksApplication, Solution.Infrastructure, $"{root}.Domain", $"{root}.Application"]);
    }

    [Fact]
    public void Controllers_go_through_handlers_and_never_touch_persistence()
    {
        var result = Types.InAssembly(Solution.AssemblyNamed(Solution.Api))
            .That().ResideInNamespace(Solution.ControllersNamespace)
            .ShouldNot().HaveDependencyOnAny([EfCore, Npgsql, Solution.Infrastructure, .. PersistenceAbstractions()])
            .GetResult();

        Assert.Empty(result.FailingTypeNames ?? []);
    }

    [Fact]
    public void A_controller_lives_in_the_folder_of_the_one_module_it_serves()
    {
        Assert.Empty(EntryPointsOutsideTheirModule(Solution.Api, Solution.ControllersNamespace, Solution.ControllersByFolder, []));
    }

    [Fact]
    public void Jobs_go_through_use_cases_and_never_touch_persistence()
    {
        var result = Types.InAssembly(Solution.AssemblyNamed(Solution.Worker))
            .That().ResideInNamespace(Solution.JobsNamespace)
            .ShouldNot().HaveDependencyOnAny([EfCore, Npgsql, .. PersistenceAbstractions()])
            .GetResult();

        Assert.Empty(result.FailingTypeNames ?? []);
    }

    [Fact]
    public void A_job_lives_in_the_folder_of_the_one_module_it_serves()
    {
        Assert.Empty(EntryPointsOutsideTheirModule(Solution.Worker, Solution.JobsNamespace, Solution.JobsByFolder, PlatformJobFolders));
    }

    [Fact]
    public void Workspace_routes_name_their_parameter_so_the_membership_filter_sees_it()
    {
        // WorkspaceScopeFilter acts on the route value "workspaceId". A route that named it differently would skip the check.
        const string expected = "workspaces/{workspaceId:guid}";

        var violations = Solution.ControllersByFolder.SelectMany(folder => folder)
            .SelectMany(controller => RouteTemplates(controller).Select(template => (Controller: controller.FullName, Template: template)))
            .Where(route => route.Template.Contains("workspaces/", StringComparison.OrdinalIgnoreCase)
                && !route.Template.Contains(expected, StringComparison.Ordinal))
            .Select(route => $"{route.Controller}: '{route.Template}' must contain '{expected}'.");

        Assert.Empty(violations);
    }

    [Fact]
    public void Building_block_domain_and_application_are_free_of_frameworks()
    {
        var violations = new List<string>();
        foreach (var name in new[] { Solution.BuildingBlocksDomain, Solution.BuildingBlocksApplication })
        {
            var result = Types.InAssembly(Solution.AssemblyNamed(name)).ShouldNot().HaveDependencyOnAny(EfCore, AspNetCore, Npgsql, Solution.Infrastructure).GetResult();
            violations.AddRange(result.FailingTypeNames ?? []);
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Money_is_never_a_floating_point_number()
    {
        Type[] forbidden = [typeof(double), typeof(float), typeof(double?), typeof(float?)];
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var violations = Solution.Modules.Append(Solution.AssemblyNamed(Solution.BuildingBlocksDomain))
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetProperties(all).Where(property => forbidden.Contains(property.PropertyType)).Select(property => $"{type.FullName}.{property.Name}")
                .Concat(type.GetFields(all).Where(field => forbidden.Contains(field.FieldType)).Select(field => $"{type.FullName}.{field.Name}")));

        Assert.Empty(violations);
    }

    [Fact]
    public void Ownership_filters_are_bypassed_only_by_allow_listed_system_code()
    {
        var violations = new List<string>();
        foreach (var assembly in Solution.Assemblies.Where(assembly => !Solution.IsBuildingBlock(Solution.NameOf(assembly))))
        {
            using var definition = AssemblyDefinition.ReadAssembly(assembly.Location);
            violations.AddRange(definition.MainModule.GetTypes()
                .Where(type => type.Methods.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                    instruction.OpCode.FlowControl == FlowControl.Call && instruction.Operand is MethodReference { Name: "IgnoreQueryFilters" })))
                .Select(type => type.DeclaringType?.FullName ?? type.FullName)
                .Where(typeName => !MayIgnoreQueryFilters.Contains(typeName))
                .Select(typeName => $"{typeName} calls IgnoreQueryFilters."));
        }

        Assert.Empty(violations);
    }

    /// <summary>The interfaces through which each module reaches its tables. Only that module's handlers may use them.</summary>
    private static IEnumerable<string> PersistenceAbstractions() =>
        Solution.ModuleNames.Select(module => $"{Solution.ModuleRoot(module)}.Application.Abstractions");

    private static List<string> EntryPointsOutsideTheirModule(string assemblyName, string rootNamespace, ILookup<string, Type> typesByFolder, string[] platformFolders)
    {
        var violations = new List<string>();
        foreach (var folder in typesByFolder.Where(folder => !platformFolders.Contains(folder.Key)))
        {
            if (!Solution.ModuleNames.Contains(folder.Key))
            {
                violations.AddRange(folder.Select(type => $"{type.FullName} is in the folder '{folder.Key}', which is not a module."));
                continue;
            }

            var otherModules = Solution.ModuleNames.Where(module => module != folder.Key).Select(Solution.ModuleRoot).ToArray();
            if (otherModules.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(Solution.AssemblyNamed(assemblyName))
                .That().ResideInNamespace($"{rootNamespace}.{folder.Key}")
                .ShouldNot().HaveDependencyOnAny(otherModules)
                .GetResult();
            violations.AddRange(result.FailingTypeNames ?? []);
        }

        return violations;
    }

    /// <summary>Every full route of a controller: its class-level template joined with each action-level one.</summary>
    private static IEnumerable<string> RouteTemplates(Type controller)
    {
        var prefixes = TemplatesOn(controller).DefaultIfEmpty(string.Empty).ToList();
        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => TemplatesOn(method).DefaultIfEmpty(string.Empty))
            .DefaultIfEmpty(string.Empty);

        return actions.SelectMany(action => action.StartsWith('/') || action.StartsWith("~/", StringComparison.Ordinal)
            ? [action]
            : prefixes.Select(prefix => $"{prefix}/{action}"));
    }

    // Read by name so this project needs no reference to ASP.NET Core: [Route], [HttpGet] and the rest all expose Template.
    private static IEnumerable<string> TemplatesOn(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true)
            .Where(attribute => attribute.GetType().GetInterfaces().Any(contract => contract.Name == "IRouteTemplateProvider"))
            .Select(attribute => attribute.GetType().GetProperty("Template")?.GetValue(attribute) as string)
            .OfType<string>();

    private static void AssertNoDependencies(string layer, Func<string, string[]> forbidden)
    {
        var violations = new List<string>();
        foreach (var module in Solution.Modules)
        {
            var root = Solution.NameOf(module);
            var result = Types.InAssembly(module)
                .That().ResideInNamespace($"{root}.{layer}")
                .ShouldNot().HaveDependencyOnAny(forbidden(root))
                .GetResult();
            violations.AddRange(result.FailingTypeNames ?? []);
        }

        Assert.Empty(violations);
    }
}
