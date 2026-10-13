namespace PersonalFinance.BuildingBlocks.Domain;

/// <summary>Ordered from least to most privileged. No role grants access to another member's private data.</summary>
public enum WorkspaceRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}
