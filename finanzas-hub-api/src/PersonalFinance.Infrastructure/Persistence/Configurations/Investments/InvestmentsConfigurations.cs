using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Investments.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Investments;

internal sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable("holdings", Schemas.Investments, table =>
            table.HasCheckConstraint("ck_holdings_type", "type IN ('MutualFund', 'TermDeposit', 'Stock', 'Etf', 'Crypto', 'Other')"));
        builder.HasKey(holding => holding.Id);
        builder.Property(holding => holding.Id).ValueGeneratedNever();
        builder.Property(holding => holding.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(holding => holding.Name).HasMaxLength(Holding.MaxNameLength).IsRequired();
        builder.Property(holding => holding.Ticker).HasMaxLength(Holding.MaxTickerLength);
        builder.Property(holding => holding.Institution).HasMaxLength(Holding.MaxInstitutionLength);
        builder.Property(holding => holding.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(holding => holding.IsClosed);
        builder.Ignore(holding => holding.HasUnits);
        builder.Ignore(holding => holding.Capital);
        builder.Ignore(holding => holding.Quantity);
        builder.Ignore(holding => holding.ProjectedValue);
        builder.HasIndex(holding => new { holding.WorkspaceId, holding.OwnerUserId, holding.ClosedOn });
        builder.HasIndex(holding => holding.AccountId).IsUnique();

        builder.OwnsOne(holding => holding.TermDeposit, terms =>
        {
            terms.Property(term => term.AnnualRate).HasColumnName("term_annual_rate").HasPrecision(9, 4);
            terms.Property(term => term.Days).HasColumnName("term_days");
            terms.Property(term => term.MaturesOn).HasColumnName("term_matures_on");
        });

        builder.OwnsMany(holding => holding.Movements, movements =>
        {
            movements.ToTable("holding_movements", Schemas.Investments, table =>
            {
                table.HasCheckConstraint("ck_holding_movements_amount", "amount > 0 AND capital >= 0");
                table.HasCheckConstraint("ck_holding_movements_kind", "kind IN ('Contribution', 'Withdrawal')");
            });
            movements.WithOwner().HasForeignKey("HoldingId");
            movements.HasKey(movement => movement.Id);
            movements.Property(movement => movement.Id).ValueGeneratedNever();
            movements.Property(movement => movement.Kind).HasConversion<string>().HasMaxLength(16);
            movements.Property(movement => movement.Amount).HasPrecision(18, 2);
            movements.Property(movement => movement.Capital).HasPrecision(18, 2);
            movements.Property(movement => movement.Quantity).HasPrecision(28, 8);
            movements.Ignore(movement => movement.Realized);
            movements.Ignore(movement => movement.SignedAmount);
        });
        builder.Navigation(holding => holding.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(holding => holding.Valuations, valuations =>
        {
            valuations.ToTable("holding_valuations", Schemas.Investments, table => table.HasCheckConstraint("ck_holding_valuations_value", "value >= 0"));
            valuations.WithOwner().HasForeignKey("HoldingId");
            valuations.HasKey(valuation => valuation.Id);
            valuations.Property(valuation => valuation.Id).ValueGeneratedNever();
            valuations.Property(valuation => valuation.Value).HasPrecision(18, 2);
            valuations.Property(valuation => valuation.UnitPrice).HasPrecision(28, 8);
        });
        builder.Navigation(holding => holding.Valuations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
