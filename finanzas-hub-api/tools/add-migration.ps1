<#
.SYNOPSIS
  Creates an EF Core migration.
.DESCRIPTION
  There is one DbContext and one migrations folder for the whole application, in PersonalFinance.Infrastructure.
  Creating a migration does not connect to the database, but the connection string must be configured
  (user-secrets or ConnectionStrings__PersonalFinance) because the host is built.
.EXAMPLE
  ./tools/add-migration.ps1 -Name AddLedgerAccounts
#>
param(
    [Parameter(Mandatory)][string]$Name
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."

dotnet tool restore | Out-Null
dotnet ef migrations add $Name `
    --context AppDbContext `
    --project "$root/src/PersonalFinance.Infrastructure" `
    --startup-project "$root/src/PersonalFinance.Api" `
    --output-dir Persistence/Migrations
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
