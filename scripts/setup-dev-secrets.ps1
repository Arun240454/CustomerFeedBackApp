<#
  Generates random development secrets and stores them with `dotnet user-secrets`
  (outside the repository). All three services share one UserSecretsId, so one run configures them all.
  Safe to re-run: existing secrets are kept unless -Force is given.
#>
param([switch]$Force)

$project = Join-Path $PSScriptRoot '..\src\CustomerFeedback.Identity.Api'

function Get-RandomBase64([int]$bytes) {
    $buffer = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    [Convert]::ToBase64String($buffer)
}

$existing = (dotnet user-secrets list --project $project) -join "`n"

if ($Force -or $existing -notmatch 'Jwt:Key') {
    dotnet user-secrets set 'Jwt:Key' (Get-RandomBase64 48) --project $project | Out-Null
    Write-Host 'Generated Jwt:Key'
}
if ($Force -or $existing -notmatch 'Encryption:Key') {
    dotnet user-secrets set 'Encryption:Key' (Get-RandomBase64 32) --project $project | Out-Null
    Write-Host 'Generated Encryption:Key'
}
Write-Host 'Development secrets are configured.'
