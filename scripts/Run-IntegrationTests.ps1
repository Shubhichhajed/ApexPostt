[CmdletBinding()]
param(
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = "Server=$SqlServer;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
}

$env:APEXPOST_TEST_CONNECTION = $ConnectionString

Write-Host "Running ApexPost integration tests against the configured SQL Server..." -ForegroundColor Cyan
Write-Host "The test factory creates and drops isolated ApexPost_Test_* databases." -ForegroundColor DarkGray

dotnet test ".\tests\ApexPost.IntegrationTests\ApexPost.IntegrationTests.csproj" --logger "console;verbosity=normal"
exit $LASTEXITCODE
