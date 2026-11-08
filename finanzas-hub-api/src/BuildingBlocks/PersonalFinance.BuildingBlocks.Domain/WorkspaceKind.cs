namespace PersonalFinance.BuildingBlocks.Domain;

/// <summary>
/// What a workspace is for (ADR-023). Chosen when it is created and never changed afterwards.
/// </summary>
public enum WorkspaceKind
{
    /// <summary>
    /// The personal workspace of a person, or one created to share with others. Has no books of its own: the private
    /// finances seen in it are those of the personal workspace of whoever looks. The only kind that admits members,
    /// goals and shared expenses.
    /// </summary>
    Personal = 0,

    /// <summary>Books of a business, kept apart from the personal ones. Only its owner uses it.</summary>
    Business = 1,

    /// <summary>Any other set of books kept apart. Only its owner uses it.</summary>
    Other = 2
}
