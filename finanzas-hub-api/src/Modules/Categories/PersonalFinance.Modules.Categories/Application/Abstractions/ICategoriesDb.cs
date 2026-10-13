using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.Modules.Categories.Application.Abstractions;

/// <summary>The tables this module may read and write. Both are private: queries only see the current person's rows.</summary>
public interface ICategoriesDb
{
    DbSet<Category> Categories { get; }

    DbSet<Tag> Tags { get; }
}
