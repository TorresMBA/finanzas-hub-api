using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", Schemas.Identity);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.DisplayName).HasMaxLength(User.MaxDisplayNameLength).IsRequired();
        builder.Property(user => user.PhoneNumber).HasMaxLength(User.MaxPhoneLength);
    }
}

internal sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("user_preferences", Schemas.Identity, table =>
        {
            table.HasCheckConstraint("ck_user_preferences_week_start", "week_start IN ('Monday', 'Sunday')");
            table.HasCheckConstraint("ck_user_preferences_theme", "theme IN ('Dark', 'Light')");
            table.HasCheckConstraint("ck_user_preferences_savings_rate_target", "savings_rate_target BETWEEN 0 AND 100");
        });
        builder.HasKey(preferences => preferences.UserId);
        builder.Property(preferences => preferences.UserId).ValueGeneratedNever();
        builder.Property(preferences => preferences.Locale).HasMaxLength(10).IsRequired();
        builder.Property(preferences => preferences.DateFormat).HasMaxLength(20).IsRequired();
        builder.Property(preferences => preferences.WeekStart).HasConversion<string>().HasMaxLength(10);
        builder.Property(preferences => preferences.Theme).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<User>().WithOne().HasForeignKey<UserPreferences>(preferences => preferences.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

// The tables ASP.NET Core Identity brings. There are none for roles: workspace roles are this application's own.
internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims", Schemas.Identity);
}

/// <summary>Sign-ins through an external provider (Google, Microsoft). Unused until that is added.</summary>
internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins", Schemas.Identity);
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens", Schemas.Identity);
}

/// <summary>Keys that protect the session cookie and the email tokens. In the database so they survive a redeploy.</summary>
internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder) => builder.ToTable("data_protection_keys", Schemas.Platform);
}
