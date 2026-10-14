using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.SharedExpenses.Domain;

public enum SplitType
{
    /// <summary>The same for everyone.</summary>
    Equal,

    /// <summary>Each one a percentage; they add up to 100.</summary>
    Percentage,

    /// <summary>Each one an amount; they add up to the total.</summary>
    FixedAmount,

    /// <summary>Each one a number of parts, like 2-1-1.</summary>
    Shares
}

/// <param name="Value">The percentage, the amount or the parts, by the kind of split. Not used for an equal one.</param>
public sealed record SplitInput(Guid UserId, decimal? Value);

/// <summary>
/// Divides a total among people. Whatever the kind of split, the parts add up to the total to the cent: the
/// cents left over by rounding go, one each, to those whose part was cut the most, in the order they were given.
/// </summary>
public static class SplitCalculator
{
    public static Result<IReadOnlyList<(Guid UserId, decimal Amount)>> Split(decimal total, SplitType type, IReadOnlyList<SplitInput> participants)
    {
        if (participants.Count == 0)
        {
            return Error.Validation("shared.split.no_participants", "Choose at least one person to split the expense among.");
        }

        if (participants.Select(participant => participant.UserId).Distinct().Count() != participants.Count)
        {
            return Error.Validation("shared.split.repeated_participant", "A person takes part in the split once.");
        }

        var values = participants.Select(participant => participant.Value ?? 0m).ToList();
        switch (type)
        {
            case SplitType.Equal:
                return Proportional(total, participants, participants.Select(_ => 1m).ToList());

            case SplitType.Percentage when values.Any(value => value < 0) || values.Sum() != 100m:
                return Error.Validation("shared.split.percentages_invalid", "The percentages must add up to 100.");

            case SplitType.Shares when values.Any(value => value <= 0):
                return Error.Validation("shared.split.shares_invalid", "Each person needs a number of parts greater than zero.");

            case SplitType.Percentage or SplitType.Shares:
                return Proportional(total, participants, values);

            default:
                return values.Any(value => value < 0 || decimal.Round(value, 2) != value) || values.Sum() != total
                    ? Error.Validation("shared.split.amounts_invalid", "The amounts must add up to the total.")
                    : participants.Select((participant, index) => (participant.UserId, values[index])).ToList();
        }
    }

    private static List<(Guid UserId, decimal Amount)> Proportional(decimal total, IReadOnlyList<SplitInput> participants, IReadOnlyList<decimal> weights)
    {
        var sum = weights.Sum();
        var exact = weights.Select(weight => total * weight / sum).ToList();
        var parts = exact.Select(amount => Math.Floor(amount * 100m) / 100m).ToList();
        var leftoverCents = (int)((total - parts.Sum()) * 100m);
        foreach (var index in Enumerable.Range(0, parts.Count).OrderByDescending(index => exact[index] - parts[index]).ThenBy(index => index).Take(leftoverCents))
        {
            parts[index] += 0.01m;
        }

        return participants.Select((participant, index) => (participant.UserId, parts[index])).ToList();
    }
}
