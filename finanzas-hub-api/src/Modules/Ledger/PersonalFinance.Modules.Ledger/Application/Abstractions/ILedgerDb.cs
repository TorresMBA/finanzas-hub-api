using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Abstractions;

/// <summary>The tables this module may read and write. All three are private: queries only see the current person's rows.</summary>
public interface ILedgerDb
{
    DbSet<Account> Accounts { get; }

    DbSet<Operation> Operations { get; }

    DbSet<Entry> Entries { get; }
}
