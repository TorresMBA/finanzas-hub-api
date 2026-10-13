<#
.SYNOPSIS
  Applies every pending migration and exits.
.EXAMPLE
  ./tools/migrate.ps1
  ./tools/migrate.ps1 -ConnectionString "Host=localhost;Database=personal_hub;Username=personal_hub;Password=..."
#>
param(
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'

if ($ConnectionString) {
    $env:ConnectionStrings__PersonalFinance = $ConnectionString
}

if (-not $env:ASPNETCORE_ENVIRONMENT) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
}

dotnet run --project "$PSScriptRoot/../src/PersonalFinance.Api" --no-launch-profile -- migrate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
