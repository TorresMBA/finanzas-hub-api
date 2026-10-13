using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Budgets.Domain;

namespace PersonalFinance.Modules.Budgets.Application.Abstractions;

/// <summary>The table this module may read and write. It is private: queries only see the current person's rows.</summary>
public interface IBudgetsDb
{
    DbSet<Budget> Budgets { get; }
}
