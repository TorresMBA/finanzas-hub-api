using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.Modules.Sample.Application.Abstractions;

/// <summary>
/// The tables this module may read and write. It exposes EF Core's own abstraction instead of wrapping it in
/// repositories; the application's DbContext implements it, so the module never references infrastructure
/// and never sees another module's tables.
/// </summary>
public interface ISampleDb
{
    DbSet<Note> Notes { get; }

    DbSet<NoteReceipt> NoteReceipts { get; }
}
