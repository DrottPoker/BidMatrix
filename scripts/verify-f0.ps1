param(
    [string]$ApiBaseUrl = "http://localhost:8080",
    [string]$OwnerEmail = "owner@example.invalid",
    [string]$OwnerPassword = "change-me-local-owner-password",
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.UseCookies = $true
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = [Uri]::new($ApiBaseUrl)
$script:csrfHeaderName = $null
$script:csrfToken = $null

function Read-JsonResponse {
    param([System.Net.Http.HttpResponseMessage]$Response)
    $body = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $Response.IsSuccessStatusCode) {
        throw "HTTP $([int]$Response.StatusCode): $body"
    }
    if ([string]::IsNullOrWhiteSpace($body)) { return $null }
    return $body | ConvertFrom-Json
}

function Get-ApiJson {
    param([string]$Path)
    $response = $client.GetAsync($Path).GetAwaiter().GetResult()
    try { return Read-JsonResponse $response } finally { $response.Dispose() }
}

function Send-ApiJson {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [string]$IdempotencyKey = ""
    )
    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        $Path)
    try {
        if ($script:csrfHeaderName) {
            $request.Headers.TryAddWithoutValidation($script:csrfHeaderName, $script:csrfToken) | Out-Null
        }
        if ($IdempotencyKey) {
            $request.Headers.TryAddWithoutValidation("Idempotency-Key", $IdempotencyKey) | Out-Null
        }
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 30 -Compress
            $request.Content = [System.Net.Http.StringContent]::new(
                $json,
                [System.Text.Encoding]::UTF8,
                "application/json")
        }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try { return Read-JsonResponse $response } finally { $response.Dispose() }
    }
    finally {
        $request.Dispose()
    }
}

try {
    $csrf = Get-ApiJson "/v1/auth/csrf"
    $script:csrfHeaderName = $csrf.headerName
    $script:csrfToken = $csrf.token
    Send-ApiJson "POST" "/v1/auth/login" @{ email = $OwnerEmail; password = $OwnerPassword } | Out-Null
    $csrf = Get-ApiJson "/v1/auth/csrf"
    $script:csrfHeaderName = $csrf.headerName
    $script:csrfToken = $csrf.token

    $analysis = Send-ApiJson "POST" "/v1/analyses" @{ title = "F1 essential end-to-end verification" } "f1-e2e-analysis-$([guid]::NewGuid())"
    $fixturePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\tests\fixtures\rfp\synthetic-it-rfp.pdf"))
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $fileContent = [System.Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($fixturePath))
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("application/pdf")
    $multipart.Add($fileContent, "file", [IO.Path]::GetFileName($fixturePath))
    $uploadRequest = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        "/v1/analyses/$($analysis.id)/files")
    $uploadRequest.Headers.TryAddWithoutValidation($script:csrfHeaderName, $script:csrfToken) | Out-Null
    $uploadRequest.Content = $multipart
    try {
        $uploadResponse = $client.SendAsync($uploadRequest).GetAwaiter().GetResult()
        try { Read-JsonResponse $uploadResponse | Out-Null } finally { $uploadResponse.Dispose() }
    }
    finally {
        $uploadRequest.Dispose()
        $multipart.Dispose()
    }
    Send-ApiJson "POST" "/v1/analyses/$($analysis.id)/submit" $null | Out-Null

    $requirements = Get-ApiJson "/v1/analyses/$($analysis.id)/requirements"
    if ($requirements.capabilityStatus -ne "notReady" -or $requirements.requirements.Count -ne 0) {
        throw "Requirements were exposed before the durable extraction workflow completed."
    }

    $demoTasks = @()
    foreach ($agentKey in @("executive", "support", "product-analyst", "engineering")) {
        $demoTasks += Send-ApiJson "POST" "/owner/v1/agent-demos/$agentKey" @{ input = $null } "f0-e2e-$agentKey-$([guid]::NewGuid())"
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $matchingRuns = @()
    do {
        $runs = (Get-ApiJson "/owner/v1/runs").runs
        $matchingRuns = @($runs | Where-Object { $demoTasks.taskId -contains $_.taskId })
        if ($matchingRuns.Count -eq 4 -and @($matchingRuns | Where-Object status -ne "completed").Count -eq 0) { break }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($matchingRuns.Count -ne 4 -or @($matchingRuns | Where-Object status -ne "completed").Count -ne 0) {
        throw "The four offline agent demonstrations did not complete within the timeout."
    }

    $analysisDeadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $analysisState = Get-ApiJson "/v1/analyses/$($analysis.id)"
        if ($analysisState.status -eq "requires_review") { break }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $analysisDeadline)
    if ($analysisState.status -ne "requires_review") {
        throw "The analysis did not reach requires_review."
    }

    $requirements = Get-ApiJson "/v1/analyses/$($analysis.id)/requirements"
    if ($requirements.capabilityStatus -ne "requiresReview" -or $requirements.extractionStatus -ne "succeeded") {
        throw "F1 extraction did not complete in a manually reviewed state."
    }
    if ($requirements.metrics.requirementCount -lt 2 -or
        $requirements.metrics.mandatoryRequirementCount -lt 2 -or
        $requirements.metrics.citedRequirementCount -ne $requirements.metrics.requirementCount) {
        throw "F1 extraction did not return the expected cited mandatory requirements."
    }
    if ($requirements.metrics.filesRequiringOcr -ne 0 -or $requirements.metrics.failedFileCount -ne 0) {
        throw "The digital F1 fixture unexpectedly requires OCR or contains failed files."
    }

    $dashboard = Get-ApiJson "/owner/v1/dashboard"
    if (-not $dashboard.draftOnly -or -not $dashboard.externalActionsDisabled -or -not $dashboard.auditChainValid) {
        throw "The F0 control or audit state is unsafe."
    }

    $engineeringTaskId = ($demoTasks | Where-Object agentKey -eq "engineering").taskId
    $engineeringSandboxCount = docker exec bidmatrix-postgres-1 psql -U bidmatrix_admin -d bidmatrix -Atc "select count(*) from engineering_sandboxes where task_id='$engineeringTaskId'"
    if ($engineeringSandboxCount -ne "1") {
        throw "The engineering demonstration did not persist its isolated sandbox."
    }

    [pscustomobject]@{
        analysisId = $analysis.id
        analysisStatus = $analysisState.status
        extractionStatus = $requirements.extractionStatus
        requirementsExtracted = $requirements.metrics.requirementCount
        mandatoryRequirements = $requirements.metrics.mandatoryRequirementCount
        citedRequirements = $requirements.metrics.citedRequirementCount
        agentRunsCompleted = $matchingRuns.Count
        engineeringSandboxRecorded = $true
        auditChainValid = $dashboard.auditChainValid
        draftOnly = $dashboard.draftOnly
        externalActionsDisabled = $dashboard.externalActionsDisabled
    } | ConvertTo-Json
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
