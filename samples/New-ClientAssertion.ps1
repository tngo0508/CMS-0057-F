<#
.SYNOPSIS
Signs the client assertion a provider system presents at the token endpoint (SMART App Launch Backend Services).

.DESCRIPTION
Reads a private RSA key in JWK form and prints a JWT with iss and sub set to the client id, aud set to the token
endpoint, exp four minutes ahead (the profile allows at most five) and a fresh jti. Each assertion can be presented
once; run the script again for the next token.

Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
./samples/New-ClientAssertion.ps1
Signs with the published, synthetic key of the sample client in appsettings.json.

.EXAMPLE
./samples/New-ClientAssertion.ps1 -PrivateKeyPath my-system.private.jwk.json -ClientId my-system -TokenEndpoint https://plan.example.org/auth/token
#>
param(
    [string] $PrivateKeyPath = (Join-Path $PSScriptRoot 'sample-provider-system.private.jwk.json'),
    [string] $ClientId,
    [string] $TokenEndpoint = 'http://localhost:5198/auth/token'
)

$jwk = Get-Content -Raw -Path $PrivateKeyPath | ConvertFrom-Json
if (-not $ClientId) { $ClientId = $jwk.client_id }
if (-not $ClientId) { throw 'Give -ClientId, or put a client_id member in the JWK file.' }

function ConvertFrom-Base64Url([string] $text) {
    $text = $text.Replace('-', '+').Replace('_', '/')
    switch ($text.Length % 4) { 2 { $text += '==' } 3 { $text += '=' } }
    [Convert]::FromBase64String($text)
}

function ConvertTo-Base64Url([byte[]] $bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$parameters = New-Object System.Security.Cryptography.RSAParameters
$parameters.Modulus = ConvertFrom-Base64Url $jwk.n
$parameters.Exponent = ConvertFrom-Base64Url $jwk.e
$parameters.D = ConvertFrom-Base64Url $jwk.d
$parameters.P = ConvertFrom-Base64Url $jwk.p
$parameters.Q = ConvertFrom-Base64Url $jwk.q
$parameters.DP = ConvertFrom-Base64Url $jwk.dp
$parameters.DQ = ConvertFrom-Base64Url $jwk.dq
$parameters.InverseQ = ConvertFrom-Base64Url $jwk.qi
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportParameters($parameters)

$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$header = [ordered]@{ alg = 'RS384'; typ = 'JWT'; kid = $jwk.kid } | ConvertTo-Json -Compress
$payload = [ordered]@{
    iss = $ClientId
    sub = $ClientId
    aud = $TokenEndpoint
    iat = $now
    exp = $now + 240
    jti = [Guid]::NewGuid().ToString('N')
} | ConvertTo-Json -Compress

$utf8 = [System.Text.Encoding]::UTF8
$signingInput = (ConvertTo-Base64Url $utf8.GetBytes($header)) + '.' + (ConvertTo-Base64Url $utf8.GetBytes($payload))
$signature = $rsa.SignData(
    $utf8.GetBytes($signingInput),
    [System.Security.Cryptography.HashAlgorithmName]::SHA384,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

"$signingInput.$(ConvertTo-Base64Url $signature)"
