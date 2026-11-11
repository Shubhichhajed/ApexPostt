# Regenerates the typed C# client from contracts/v2.json.
# The normal path is simply building ApexPost.Client: its OpenApiReference runs NSwag on every build.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet build src/ApexPost.Client/ApexPost.Client.csproj -c Debug
Write-Host 'Generated client written under src/ApexPost.Client/obj/ (regenerated on every build, never edited by hand)'

# Optional: produce the same output with the NSwag CLI and nswag.json.
if (Get-Command nswag -ErrorAction SilentlyContinue) {
    nswag run nswag.json
}
