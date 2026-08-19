[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

. (Join-Path $PSScriptRoot "Import-BetterAuthUserSecrets.ps1")

if ([string]::IsNullOrWhiteSpace($env:BIDMATRIX_ENVIRONMENT)) {
    $env:BIDMATRIX_ENVIRONMENT = "Development"
}

Push-Location (Join-Path $repositoryRoot "apps\web")
try {
    npm run dev
}
finally {
    Pop-Location
}
