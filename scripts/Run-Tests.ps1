$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
dotnet restore ApexPost.sln
dotnet test ApexPost.sln --configuration Release --logger "trx;LogFileName=ApexPost-Tests.trx" --results-directory TestResults
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
