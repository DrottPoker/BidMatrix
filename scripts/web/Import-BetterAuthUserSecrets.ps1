[CmdletBinding()]
param(
    [string] $Project
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = Join-Path $PSScriptRoot "..\..\src\backend\BidMatrix.Api"
}

$requiredKeys = @(
    "BETTER_AUTH_DATABASE_URL",
    "BETTER_AUTH_SECRET",
    "BETTER_AUTH_TRUSTED_ORIGINS",
    "BETTER_AUTH_URL",
    "BETTER_AUTH_SMTP_HOST",
    "BETTER_AUTH_SMTP_PORT",
    "BETTER_AUTH_SMTP_SECURE",
    "BETTER_AUTH_SMTP_REQUIRE_TLS",
    "BETTER_AUTH_EMAIL_FROM_ADDRESS"
)
$optionalKeys = @(
    "BETTER_AUTH_OWNER_BOOTSTRAP_SECRET",
    "BETTER_AUTH_OWNER_EMAIL",
    "BETTER_AUTH_OWNER_NAME",
    "BETTER_AUTH_OWNER_PASSWORD",
    "BETTER_AUTH_GOOGLE_CLIENT_ID",
    "BETTER_AUTH_GOOGLE_CLIENT_SECRET",
    "BETTER_AUTH_GITHUB_CLIENT_ID",
    "BETTER_AUTH_GITHUB_CLIENT_SECRET",
    "BETTER_AUTH_SMTP_USER",
    "BETTER_AUTH_SMTP_PASSWORD",
    "BETTER_AUTH_EMAIL_FROM_NAME"
)
$values = @{}

if ([string]::IsNullOrWhiteSpace($env:BIDMATRIX_ENVIRONMENT)) {
    $env:BIDMATRIX_ENVIRONMENT = "Development"
}

dotnet user-secrets list --project $Project | ForEach-Object {
    $parts = $_ -split " = ", 2
    if ($parts.Length -eq 2) {
        $values[$parts[0]] = $parts[1]
    }
}

foreach ($key in $requiredKeys) {
    if (-not $values.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($values[$key])) {
        throw "$key is missing from the BidMatrix API User Secrets store."
    }

    [Environment]::SetEnvironmentVariable($key, $values[$key], "Process")
}

foreach ($key in $optionalKeys) {
    if ($values.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($values[$key])) {
        [Environment]::SetEnvironmentVariable($key, $values[$key], "Process")
    }
}
