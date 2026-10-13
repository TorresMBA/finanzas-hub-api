using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Scheduling.Domain;

namespace PersonalFinance.Modules.Scheduling.Application.Abstractions;

/// <summary>The table this module may read and write. It is private: queries only see the current person's rules.</summary>
public interface ISchedulingDb
{
    DbSet<RecurringRule> RecurringRules { get; }
}
