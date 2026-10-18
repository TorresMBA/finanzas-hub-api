namespace PersonalFinance.Modules.SharedExpenses.Contracts;

/// <summary>A group of the current person, as the search finds it.</summary>
public sealed record SharedGroupMatch(Guid Id, string Name, string Icon, bool IsArchived);

/// <summary>What other modules read from the shared expenses. Only groups the current person is in are ever answered.</summary>
public interface ISharedGroupQueries
{
    /// <summary>The groups whose name contains the text, the ones in use first.</summary>
    Task<IReadOnlyList<SharedGroupMatch>> SearchAsync(string text, int count, CancellationToken cancellationToken);
}
