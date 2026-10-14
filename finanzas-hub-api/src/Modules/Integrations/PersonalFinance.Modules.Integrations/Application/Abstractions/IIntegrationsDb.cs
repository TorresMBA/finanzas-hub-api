using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Integrations.Domain;

namespace PersonalFinance.Modules.Integrations.Application.Abstractions;

/// <summary>The tables this module may read and write. All of them are private: queries only see the current person's rows.</summary>
public interface IIntegrationsDb
{
    DbSet<ImportSession> ImportSessions { get; }

    DbSet<ImportRecord> ImportRecords { get; }

    DbSet<CategorizationRule> CategorizationRules { get; }
}
