[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\BlueBridge.csproj'
$output = Join-Path $PSScriptRoot 'artifacts\publish'

dotnet restore $project -r win-x64
dotnet publish $project -c Release -r win-x64 --self-contained true -p:TreatWarningsAsErrors=true -o $output --no-restore

Write-Host "BlueBridge was published to $output" -ForegroundColor Green
