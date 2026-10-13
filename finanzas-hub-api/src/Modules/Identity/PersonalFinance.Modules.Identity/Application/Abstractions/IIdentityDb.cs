using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Modules.Identity.Application.Abstractions;

/// <summary>The tables this module may read and write.</summary>
public interface IIdentityDb
{
    DbSet<User> Users { get; }

    DbSet<UserPreferences> UserPreferences { get; }
}
