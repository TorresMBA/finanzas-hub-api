<#
.SYNOPSIS
  Regenerates docs/api/openapi.v1.json from the running API. Run it after an intended API change,
  then regenerate the frontend client (npm run api:generate in finanzas-hub-web).
.DESCRIPTION
  Needs a PostgreSQL like any integration test: Docker, or PERSONALFINANCE_TEST_POSTGRES pointing to a server.
#>
$ErrorActionPreference = 'Stop'

$env:UPDATE_OPENAPI_SNAPSHOT = '1'
try {
    dotnet test "$PSScriptRoot/../tests/PersonalFinance.IntegrationTests" --filter "FullyQualifiedName~OpenApiContractTests"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Remove-Item Env:UPDATE_OPENAPI_SNAPSHOT -ErrorAction SilentlyContinue
}
