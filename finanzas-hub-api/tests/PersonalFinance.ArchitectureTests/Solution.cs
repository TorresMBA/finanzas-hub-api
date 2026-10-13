using System.Reflection;
using System.Xml.Linq;

namespace PersonalFinance.ArchitectureTests;

/// <summary>Discovers what the solution is made of, so a new module is covered by every rule without editing the tests.</summary>
internal static class Solution
{
    public const string Api = "PersonalFinance.Api";
    public const string Worker = "PersonalFinance.Worker";
    public const string Infrastructure = "PersonalFinance.Infrastructure";
    public const string BuildingBlocksDomain = "PersonalFinance.BuildingBlocks.Domain";
    public const string BuildingBlocksApplication = "PersonalFinance.BuildingBlocks.Application";
    public const string ControllersNamespace = "PersonalFinance.Api.Controllers";
    public const string JobsNamespace = "PersonalFinance.Worker.Jobs";

    private const string Prefix = "PersonalFinance.";
    private const string ModulePrefix = "PersonalFinance.Modules.";

    public static string RootDirectory { get; } = FindRoot();

    /// <summary>Every assembly of the solution that ships, reached from the two hosts.</summary>
    public static IReadOnlyList<Assembly> Assemblies { get; } = LoadAssemblies();

    public static IReadOnlyList<Assembly> Modules { get; } =
        Assemblies.Where(assembly => IsModule(NameOf(assembly))).ToList();

    public static IReadOnlyList<string> ModuleNames { get; } =
        Modules.Select(module => ModuleNameOf(NameOf(module))).ToList();

    public static IReadOnlyList<ProjectFile> Projects { get; } =
        Directory.EnumerateFiles(Path.Combine(RootDirectory, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(ProjectFile.Load)
            .ToList();

    /// <summary>Controllers by the folder they live in: <c>Controllers/Ledger/AccountsController</c> is under "Ledger".</summary>
    public static ILookup<string, Type> ControllersByFolder { get; } = TypesByFolder(Api, ControllersNamespace);

    /// <summary>Jobs by the folder they live in: <c>Jobs/Scheduling/RunDueOccurrencesJob</c> is under "Scheduling".</summary>
    public static ILookup<string, Type> JobsByFolder { get; } = TypesByFolder(Worker, JobsNamespace);

    public static Assembly AssemblyNamed(string name) => Assemblies.Single(assembly => NameOf(assembly) == name);

    public static string NameOf(Assembly assembly) => assembly.GetName().Name!;

    public static bool IsModule(string name) => name.StartsWith(ModulePrefix, StringComparison.Ordinal);

    public static bool IsBuildingBlock(string name) => name.StartsWith("PersonalFinance.BuildingBlocks.", StringComparison.Ordinal);

    /// <summary>"PersonalFinance.Modules.Ledger" gives "Ledger".</summary>
    public static string ModuleNameOf(string name) => name[ModulePrefix.Length..].Split('.')[0];

    public static string ModuleRoot(string moduleName) => ModulePrefix + moduleName;

    private static ILookup<string, Type> TypesByFolder(string assemblyName, string rootNamespace) =>
        AssemblyNamed(assemblyName).GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false }
                && type.Namespace?.StartsWith(rootNamespace + ".", StringComparison.Ordinal) == true)
            .ToLookup(type => type.Namespace![(rootNamespace.Length + 1)..].Split('.')[0]);

    private static List<Assembly> LoadAssemblies()
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([Assembly.Load(Api), Assembly.Load(Worker)]);
        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            if (!loaded.TryAdd(NameOf(assembly), assembly))
            {
                continue;
            }

            foreach (var reference in assembly.GetReferencedAssemblies().Where(reference => reference.Name!.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                pending.Enqueue(Assembly.Load(reference));
            }
        }

        return loaded.Values.ToList();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PersonalFinance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("PersonalFinance.slnx was not found above the test output directory.");
    }
}

/// <summary>What a project declares in its .csproj, which is stricter than what the compiler ends up using.</summary>
internal sealed record ProjectFile(string Name, IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> PackageReferences, IReadOnlyList<string> FrameworkReferences)
{
    public static ProjectFile Load(string path)
    {
        var document = XDocument.Parse(File.ReadAllText(path));
        return new ProjectFile(
            Path.GetFileNameWithoutExtension(path),
            Includes(document, "ProjectReference").Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))).ToList(),
            Includes(document, "PackageReference"),
            Includes(document, "FrameworkReference"));
    }

    private static List<string> Includes(XDocument document, string element) =>
        document.Descendants(element).Select(node => node.Attribute("Include")?.Value).OfType<string>().ToList();
}
