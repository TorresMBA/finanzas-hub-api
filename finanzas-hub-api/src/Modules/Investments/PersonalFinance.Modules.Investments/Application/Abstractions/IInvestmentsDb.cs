using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Investments.Domain;

namespace PersonalFinance.Modules.Investments.Application.Abstractions;

/// <summary>The tables of this module. Holdings belong to a person: the global filter limits them to the current one.</summary>
public interface IInvestmentsDb
{
    DbSet<Holding> Holdings { get; }
}
