param(
    [string]$ApiBaseUrl = "http://localhost:8080",
    [string]$OwnerEmail = "owner@example.invalid",
    [string]$OwnerPassword = "change-me-local-owner-password",
    [int]$TimeoutSeconds = 120,
    [string]$ComposeProjectName = "bidmatrix",
    [string]$EnvFile = ".env"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$composeFile = Join-Path $repoRoot "compose.yaml"
$resolvedEnvFile = if ([IO.Path]::IsPathRooted($EnvFile)) {
    [IO.Path]::GetFullPath($EnvFile)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $EnvFile))
}

if ($ComposeProjectName -notmatch '^[a-z0-9][a-z0-9_-]*$') {
    throw "ComposeProjectName contains unsupported characters."
}
if (-not (Test-Path -LiteralPath $resolvedEnvFile -PathType Leaf)) {
    throw "The Compose environment file does not exist: $resolvedEnvFile"
}
$verificationStartedAt = [DateTimeOffset]::UtcNow

function Invoke-PostgresScalar {
    param([string]$Query)

    $encodedQuery = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Query))
    $arguments = @(
        "compose",
        "--project-name", $ComposeProjectName,
        "--project-directory", $repoRoot,
        "--file", $composeFile,
        "--env-file", $resolvedEnvFile,
        "exec", "-T",
        "-e", "SQL_BASE64=$encodedQuery",
        "postgres",
        "sh", "-ec",
        'printf "%s" "$SQL_BASE64" | base64 -d | PGPASSWORD="$POSTGRES_PASSWORD" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atq'
    )
    $output = & docker @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL verification failed: $($output -join [Environment]::NewLine)"
    }

    $lines = @($output | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ })
    if ($lines.Count -eq 0) { return "" }
    return $lines[-1]
}

function Assert-NoRuntimeErrors {
    $arguments = @(
        "compose",
        "--project-name", $ComposeProjectName,
        "--project-directory", $repoRoot,
        "--file", $composeFile,
        "--env-file", $resolvedEnvFile,
        "logs", "--no-color",
        "--since", $verificationStartedAt.ToString("o"),
        "api", "agent-worker", "web"
    )
    $logs = & docker @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Runtime log verification failed: $($logs -join [Environment]::NewLine)"
    }

    $errorLines = @($logs | Where-Object {
        $_ -match '(?i)(\|\s+fail:|\sERROR\s|unhandled exception|\bfatal\b)'
    })
    if ($errorLines.Count -gt 0) {
        throw "Runtime logs contain error-level entries: $($errorLines -join [Environment]::NewLine)"
    }
}

function Assert-Citation {
    param(
        [object]$Citation,
        [string]$Label
    )

    if ($null -eq $Citation -or
        [string]::IsNullOrWhiteSpace($Citation.originalFileName) -or
        $Citation.pageNumber -lt 1 -or
        [string]::IsNullOrWhiteSpace($Citation.quoteText)) {
        throw "$Label does not contain an exact file, page, and quote citation."
    }
}

$f0Gate = Join-Path $PSScriptRoot "verify-f0.ps1"
$f0Output = & $f0Gate `
    -ApiBaseUrl $ApiBaseUrl `
    -OwnerEmail $OwnerEmail `
    -OwnerPassword $OwnerPassword `
    -TimeoutSeconds $TimeoutSeconds `
    -ComposeProjectName $ComposeProjectName `
    -EnvFile $resolvedEnvFile
$f0Result = ($f0Output -join [Environment]::NewLine) | ConvertFrom-Json

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

function New-ApiRequest {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [string]$IdempotencyKey = ""
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        $Path)
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
    return $request
}

function Send-ApiJson {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [string]$IdempotencyKey = ""
    )

    $request = New-ApiRequest $Method $Path $Body $IdempotencyKey
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try { return Read-JsonResponse $response } finally { $response.Dispose() }
    }
    finally {
        $request.Dispose()
    }
}

function Send-ApiJsonExpectStatus {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [int]$ExpectedStatus
    )

    $request = New-ApiRequest $Method $Path $Body
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ([int]$response.StatusCode -ne $ExpectedStatus) {
                throw "Expected HTTP $ExpectedStatus from $Path but received $([int]$response.StatusCode): $body"
            }
            if ([string]::IsNullOrWhiteSpace($body)) { return $null }
            return $body | ConvertFrom-Json
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Upload-AnalysisPdf {
    param(
        [string]$AnalysisId,
        [string]$FixturePath
    )

    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $fileContent = [System.Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($FixturePath))
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("application/pdf")
    $multipart.Add($fileContent, "file", [IO.Path]::GetFileName($FixturePath))
    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        "/v1/analyses/$AnalysisId/files")
    $request.Headers.TryAddWithoutValidation($script:csrfHeaderName, $script:csrfToken) | Out-Null
    $request.Content = $multipart
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try { Read-JsonResponse $response | Out-Null } finally { $response.Dispose() }
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

    $analysis = Send-ApiJson "POST" "/v1/analyses" @{ title = "F2 S0 end-to-end verification" } "f2-s0-$([guid]::NewGuid())"
    $fixturePath = [IO.Path]::GetFullPath((Join-Path $repoRoot "output\pdf\bidmatrix-managed-it-cybersecurity-test-rfp.pdf"))
    Upload-AnalysisPdf $analysis.id $fixturePath
    Send-ApiJson "POST" "/v1/analyses/$($analysis.id)/submit" $null | Out-Null

    $customerBeforePublication = Get-ApiJson "/v1/analyses/$($analysis.id)/requirements"
    if (@($customerBeforePublication.requirements).Count -ne 0 -or
        @($customerBeforePublication.keyDates).Count -ne 0 -or
        @($customerBeforePublication.requestedDocuments).Count -ne 0 -or
        @($customerBeforePublication.evaluationCriteria).Count -ne 0) {
        throw "Customer results were exposed before owner publication."
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $analysisState = Get-ApiJson "/v1/analyses/$($analysis.id)"
        if ($analysisState.status -eq "requires_review") { break }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($analysisState.status -ne "requires_review") {
        throw "The F2 analysis did not reach requires_review."
    }

    $ownerReview = Get-ApiJson "/owner/v1/analyses/$($analysis.id)/review"
    $ownerRequirements = @($ownerReview.requirements)
    $ownerKeyDates = @($ownerReview.keyDates)
    $ownerRequestedDocuments = @($ownerReview.requestedDocuments)
    $ownerEvaluationCriteria = @($ownerReview.evaluationCriteria)
    if ($ownerRequirements.Count -lt 2 -or
        $ownerKeyDates.Count -lt 1 -or
        $ownerRequestedDocuments.Count -lt 1 -or
        $ownerEvaluationCriteria.Count -lt 1) {
        throw "F2 extraction did not produce every required review collection."
    }

    foreach ($requirement in $ownerRequirements) {
        $citations = @($requirement.citations)
        if ($citations.Count -eq 0) {
            throw "Requirement $($requirement.id) has no source citation."
        }
        foreach ($citation in $citations) {
            Assert-Citation $citation "Requirement $($requirement.id)"
        }
    }
    foreach ($finding in @($ownerKeyDates + $ownerRequestedDocuments + $ownerEvaluationCriteria)) {
        Assert-Citation $finding.citation "Finding $($finding.id)"
    }

    $correctedRequirement = $ownerRequirements[0]
    $correctedText = "$($correctedRequirement.requirementText) Owner-verified for S0."
    $correctionBody = @{
        requirementText = $correctedText
        category = $correctedRequirement.category
        mandatory = $correctedRequirement.mandatory
        reviewStatus = "corrected"
        correctionNote = "S0 verifies versioned owner correction capture."
        expectedVersion = $correctedRequirement.version
    }
    $afterCorrection = Send-ApiJson "PATCH" "/owner/v1/analyses/$($analysis.id)/requirements/$($correctedRequirement.id)" $correctionBody
    if ($afterCorrection.publication.correctionCount -ne 1) {
        throw "The F2 correction counter was not updated."
    }
    Send-ApiJsonExpectStatus `
        "PATCH" `
        "/owner/v1/analyses/$($analysis.id)/requirements/$($correctedRequirement.id)" `
        $correctionBody `
        409 | Out-Null

    $rejectedRequirement = $ownerRequirements[1]
    Send-ApiJson "PATCH" "/owner/v1/analyses/$($analysis.id)/requirements/$($rejectedRequirement.id)" @{
        requirementText = $rejectedRequirement.requirementText
        category = $rejectedRequirement.category
        mandatory = $rejectedRequirement.mandatory
        reviewStatus = "rejected"
        correctionNote = $null
        expectedVersion = $rejectedRequirement.version
    } | Out-Null

    $reviewNote = "S0 verified source citations, correction history, rejection filtering, and customer delivery."
    $published = Send-ApiJson "POST" "/owner/v1/analyses/$($analysis.id)/publish" @{
        reviewNote = $reviewNote
        confirmation = "PUBLISH REVIEWED ANALYSIS"
    }
    if (-not $published.publication.isPublished -or
        $published.publication.analysisStatus -ne "completed" -or
        $published.publication.reviewNote -ne $reviewNote -or
        $published.publication.correctionCount -ne 1 -or
        $published.metrics.pendingReviewCount -ne 0) {
        throw "The owner publication gate did not persist the expected F2 state."
    }

    $customerResult = Get-ApiJson "/v1/analyses/$($analysis.id)/requirements"
    $customerRequirements = @($customerResult.requirements)
    if ($customerResult.capabilityStatus -ne "ready" -or
        -not $customerResult.publication.isPublished -or
        $customerRequirements.Count -ne ($ownerRequirements.Count - 1) -or
        @($customerResult.keyDates).Count -ne $ownerKeyDates.Count -or
        @($customerResult.requestedDocuments).Count -ne $ownerRequestedDocuments.Count -or
        @($customerResult.evaluationCriteria).Count -ne $ownerEvaluationCriteria.Count) {
        throw "The published customer report does not contain the expected F2 collections."
    }
    if (@($customerRequirements | Where-Object id -eq $rejectedRequirement.id).Count -ne 0) {
        throw "A rejected requirement was exposed to the customer."
    }
    $customerCorrection = @($customerRequirements | Where-Object id -eq $correctedRequirement.id)
    if ($customerCorrection.Count -ne 1 -or
        $customerCorrection[0].requirementText -ne $correctedText -or
        $customerCorrection[0].originalRequirementText -ne $correctedRequirement.originalRequirementText -or
        $customerCorrection[0].reviewStatus -ne "corrected") {
        throw "The published correction did not preserve both corrected and original content."
    }
    foreach ($requirement in $customerRequirements) {
        foreach ($citation in @($requirement.citations)) {
            Assert-Citation $citation "Published requirement $($requirement.id)"
        }
    }
    foreach ($finding in @(@($customerResult.keyDates) + @($customerResult.requestedDocuments) + @($customerResult.evaluationCriteria))) {
        Assert-Citation $finding.citation "Published finding $($finding.id)"
    }

    $completedAnalysis = Get-ApiJson "/v1/analyses/$($analysis.id)"
    if ($completedAnalysis.status -ne "completed" -or $completedAnalysis.requiresHumanReview) {
        throw "The published analysis did not reach the completed customer state."
    }

    $migrationCount = Invoke-PostgresScalar "select count(*) from schema_migrations where version='0009_f2_reviewable_results' and checksum is not null"
    if ($migrationCount -ne "1") {
        throw "Migration 0009_f2_reviewable_results is not recorded with a checksum."
    }
    $auditCount = Invoke-PostgresScalar "select count(*) from audit_events where target_type='analysis' and target_id='$($analysis.id)' and action in ('analysis.requirement_reviewed','analysis.published')"
    if ($auditCount -ne "3") {
        throw "The F2 correction, rejection, and publication audit events were not all recorded."
    }
    Assert-NoRuntimeErrors

    [pscustomobject]@{
        foundationAnalysisId = $f0Result.analysisId
        f2AnalysisId = $analysis.id
        migration0009Applied = $true
        analysisStatus = $completedAnalysis.status
        extractionStatus = $customerResult.extractionStatus
        requirementsPublished = $customerRequirements.Count
        keyDatesPublished = @($customerResult.keyDates).Count
        requestedDocumentsPublished = @($customerResult.requestedDocuments).Count
        evaluationCriteriaPublished = @($customerResult.evaluationCriteria).Count
        correctionCount = $customerResult.publication.correctionCount
        rejectedRequirementHidden = $true
        optimisticConcurrencyVerified = $true
        auditEventsVerified = [int]$auditCount
        runtimeErrorLogMatches = 0
        auditChainValid = $f0Result.auditChainValid
        externalActionsDisabled = $f0Result.externalActionsDisabled
    } | ConvertTo-Json
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
