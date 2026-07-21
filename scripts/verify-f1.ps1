param(
    [string]$ApiBaseUrl = "http://localhost:8080",
    [string]$OwnerEmail = "owner@example.invalid",
    [string]$OwnerPassword = "change-me-local-owner-password",
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$gatePath = Join-Path $PSScriptRoot "verify-f0.ps1"

& $gatePath `
    -ApiBaseUrl $ApiBaseUrl `
    -OwnerEmail $OwnerEmail `
    -OwnerPassword $OwnerPassword `
    -TimeoutSeconds $TimeoutSeconds
