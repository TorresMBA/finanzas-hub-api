using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Goals.Domain;

namespace PersonalFinance.Modules.Goals.Application.Abstractions;

/// <summary>
/// The table this module may read and write. A goal is shared by participation: the context's global filter
/// only lets through the goals of the current workspace in which the current person is an active participant.
/// </summary>
public interface IGoalsDb
{
    DbSet<Goal> Goals { get; }
}
