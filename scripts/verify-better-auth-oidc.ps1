[CmdletBinding()]
param(
    [string] $ApiBaseUrl = "http://localhost:8080",
    [string] $WebBaseUrl = "http://localhost:3000",
    [string] $Project
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = Join-Path $PSScriptRoot "..\src\backend\BidMatrix.Api"
}

$secrets = @{}
dotnet user-secrets list --project $Project | ForEach-Object {
    $parts = $_ -split " = ", 2
    if ($parts.Length -eq 2) {
        $secrets[$parts[0]] = $parts[1]
    }
}

$ownerEmail = $secrets["BETTER_AUTH_OWNER_EMAIL"]
$ownerPassword = $secrets["BETTER_AUTH_OWNER_PASSWORD"]
if ([string]::IsNullOrWhiteSpace($ownerEmail) -or
    [string]::IsNullOrWhiteSpace($ownerPassword)) {
    throw "Better Auth owner credentials are missing from User Secrets."
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $true
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)

function Send-Request {
    param(
        [System.Net.Http.HttpMethod] $Method,
        [Uri] $Uri,
        [object] $Body,
        [hashtable] $Headers = @{}
    )

    $request = [System.Net.Http.HttpRequestMessage]::new($Method, $Uri)
    foreach ($entry in $Headers.GetEnumerator()) {
        $request.Headers.TryAddWithoutValidation($entry.Key, $entry.Value) | Out-Null
    }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 20 -Compress
        $request.Content = [System.Net.Http.StringContent]::new(
            $json,
            [Text.Encoding]::UTF8,
            "application/json")
    }

    try {
        return $client.SendAsync($request).GetAwaiter().GetResult()
    }
    finally {
        $request.Dispose()
    }
}

function Read-Json {
    param(
        [System.Net.Http.HttpResponseMessage] $Response,
        [int] $ExpectedStatus
    )

    $body = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$Response.StatusCode -ne $ExpectedStatus) {
        throw "Expected HTTP $ExpectedStatus but received $([int]$Response.StatusCode)."
    }
    if ([string]::IsNullOrWhiteSpace($body)) { return $null }
    return $body | ConvertFrom-Json
}

function Resolve-Location {
    param(
        [Uri] $RequestUri,
        [System.Net.Http.HttpResponseMessage] $Response
    )

    if ([int]$Response.StatusCode -lt 300 -or [int]$Response.StatusCode -ge 400) {
        throw "Expected a redirect but received HTTP $([int]$Response.StatusCode)."
    }
    $location = $Response.Headers.Location
    if ($null -eq $location) { throw "Redirect response did not contain Location." }
    return [Uri]::new($RequestUri, $location)
}

try {
    $discoveryUri = [Uri]::new("$WebBaseUrl/api/auth/.well-known/openid-configuration")
    $discoveryResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $discoveryUri $null
    try { $discovery = Read-Json $discoveryResponse 200 } finally { $discoveryResponse.Dispose() }
    if ($discovery.issuer -ne "$WebBaseUrl/api/auth" -or
        $discovery.authorization_endpoint -ne "$WebBaseUrl/api/auth/oauth2/authorize" -or
        $discovery.token_endpoint -ne "$WebBaseUrl/api/auth/oauth2/token") {
        throw "Better Auth OIDC discovery metadata is inconsistent."
    }

    $jwksResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) ([Uri]$discovery.jwks_uri) $null
    try { $jwks = Read-Json $jwksResponse 200 } finally { $jwksResponse.Dispose() }
    if (@($jwks.keys).Count -lt 1) { throw "Better Auth did not publish a signing key." }

    $csrfUri = [Uri]::new("$ApiBaseUrl/v1/auth/csrf")
    $csrfResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $csrfUri $null
    try { $csrf = Read-Json $csrfResponse 200 } finally { $csrfResponse.Dispose() }

    $loginUri = [Uri]::new("$ApiBaseUrl/v1/auth/login")
    $loginResponse = Send-Request ([System.Net.Http.HttpMethod]::Post) $loginUri @{
        email = $ownerEmail
        password = $ownerPassword
    } @{ $csrf.headerName = $csrf.token }
    try { Read-Json $loginResponse 200 | Out-Null } finally { $loginResponse.Dispose() }

    $identitiesUri = [Uri]::new("$ApiBaseUrl/v1/auth/federated-identities")
    $linkUri = [Uri]::new("$ApiBaseUrl/v1/auth/oidc/link?returnUrl=%2Fapp%2Faccount")
    $linkResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $linkUri $null
    try {
        $authorizeUri = Resolve-Location $linkUri $linkResponse
        $linkSetCookieHeaders = @($linkResponse.Headers.GetValues("Set-Cookie"))
    }
    finally {
        $linkResponse.Dispose()
    }
    # Windows PowerShell rejects callback-scoped cookies set by a challenge URL.
    $callbackUriForCookies = [Uri]::new("$ApiBaseUrl/signin-oidc")
    foreach ($setCookieHeader in $linkSetCookieHeaders) {
        $cookieName = (($setCookieHeader -split ";", 2)[0] -split "=", 2)[0]
        if ($cookieName.StartsWith(".AspNetCore.Correlation.", [StringComparison]::Ordinal) -or
            $cookieName.StartsWith(".AspNetCore.OpenIdConnect.Nonce.", [StringComparison]::Ordinal)) {
            $handler.CookieContainer.SetCookies($callbackUriForCookies, $setCookieHeader)
        }
    }
    $correlationCookie = @(
        $handler.CookieContainer.GetCookies($callbackUriForCookies) |
            Where-Object { $_.Name.StartsWith(".AspNetCore.Correlation.", [StringComparison]::Ordinal) }
    ) | Select-Object -First 1
    if ($null -eq $correlationCookie) {
        throw "BidMatrix did not persist its OIDC correlation cookie."
    }
    if ($authorizeUri.AbsolutePath -ne "/api/auth/oauth2/authorize") {
        throw "BidMatrix did not redirect to the Better Auth authorization endpoint."
    }

    $authorizeResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $authorizeUri $null
    try { $providerLoginUri = Resolve-Location $authorizeUri $authorizeResponse } finally { $authorizeResponse.Dispose() }
    if ($providerLoginUri.AbsolutePath -ne "/login" -or
        -not $providerLoginUri.Query.Contains("sig=")) {
        throw "Better Auth did not issue a signed login continuation."
    }

    $providerSignInUri = [Uri]::new("$WebBaseUrl/api/auth/sign-in/email")
    $providerSignInResponse = Send-Request ([System.Net.Http.HttpMethod]::Post) $providerSignInUri @{
        email = $ownerEmail
        password = $ownerPassword
        rememberMe = $false
        oauth_query = $providerLoginUri.Query.TrimStart("?")
    } @{
        Accept = "application/json"
        Origin = $WebBaseUrl
    }
    try {
        $providerBody = Read-Json $providerSignInResponse 200
    }
    finally {
        $providerSignInResponse.Dispose()
    }

    $callbackValue = if ($providerBody.url) {
        $providerBody.url
    }
    elseif ($providerBody.redirect_uri) {
        $providerBody.redirect_uri
    }
    else {
        throw "Better Auth sign-in did not continue the OIDC authorization."
    }
    $callbackUri = [Uri]::new($callbackValue)
    if ($callbackUri.AbsolutePath -ne "/signin-oidc") {
        throw "Better Auth returned an unexpected OIDC callback."
    }
    $callbackCookieNames = @(
        $handler.CookieContainer.GetCookies($callbackUri) |
            ForEach-Object { $_.Name }
    )
    if (-not ($callbackCookieNames -contains $correlationCookie.Name)) {
        throw "The OIDC correlation cookie was lost before the BidMatrix callback."
    }

    $callbackResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $callbackUri $null
    try { $completeUri = Resolve-Location $callbackUri $callbackResponse } finally { $callbackResponse.Dispose() }
    if ($completeUri.AbsolutePath -eq "/v1/auth/oidc/complete") {
        $completeResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $completeUri $null
        try { $publicReturnUri = Resolve-Location $completeUri $completeResponse } finally { $completeResponse.Dispose() }
    }
    elseif ($completeUri.AbsolutePath -eq "/app/account") {
        $publicReturnUri = $completeUri
    }
    else {
        throw "The ASP.NET OIDC handler continued to unexpected path '$($completeUri.AbsolutePath)'."
    }
    if ($publicReturnUri.AbsolutePath -ne "/app/account") {
        throw "BidMatrix did not return to the requested account page."
    }
    $returnQuery = [System.Web.HttpUtility]::ParseQueryString($publicReturnUri.Query)
    if (-not [string]::IsNullOrWhiteSpace($returnQuery["identityError"])) {
        throw "BidMatrix rejected the Better Auth owner link with '$($returnQuery["identityError"])'."
    }

    $meUri = [Uri]::new("$ApiBaseUrl/v1/me")
    $meResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $meUri $null
    try { $me = Read-Json $meResponse 200 } finally { $meResponse.Dispose() }
    if ($me.email -ne $ownerEmail -or
        -not (@($me.platformRoles) -contains "platform_owner")) {
        throw "The Better Auth owner session did not retain BidMatrix owner authority."
    }

    $identitiesAfterResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $identitiesUri $null
    try { $identitiesAfter = Read-Json $identitiesAfterResponse 200 } finally { $identitiesAfterResponse.Dispose() }
    $activeOwnerIdentity = @(
        $identitiesAfter.identities |
            Where-Object { $_.status -eq "active" -and $_.emailAtLink -eq $ownerEmail }
    ) | Select-Object -First 1
    if ($null -eq $activeOwnerIdentity) {
        throw "Better Auth did not create or preserve the active owner identity mapping."
    }

    $ownerProbeUri = [Uri]::new("$ApiBaseUrl/owner/v1/audit")
    $ownerProbeResponse = Send-Request ([System.Net.Http.HttpMethod]::Get) $ownerProbeUri $null
    try {
        Read-Json $ownerProbeResponse 200 | Out-Null
    }
    finally {
        $ownerProbeResponse.Dispose()
    }

    [pscustomobject]@{
        discoveryVerified = $true
        signingKeyPublished = $true
        authorizationRedirectVerified = $true
        signedLoginContinuationVerified = $true
        oidcCodeFlowVerified = $true
        passwordOwnerLinkVerified = $true
        bidMatrixOwnerAuthorityVerified = $true
        federatedIdentityMappingVerified = $true
        passkeyRequired = $false
    } | ConvertTo-Json
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
