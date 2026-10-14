using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.SharedExpenses.Domain;

namespace PersonalFinance.Modules.SharedExpenses.Application.Abstractions;

/// <summary>
/// The table this module may read and write. A group is shared by membership of the group: the context's
/// global filter only lets through the groups of the current workspace the current person is an active member of.
/// </summary>
public interface ISharedExpensesDb
{
    DbSet<SharedGroup> SharedGroups { get; }
}
