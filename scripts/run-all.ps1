<#
  Starts the three backend services and the React dev server, each in its own window.
  Prerequisites: scripts/setup-dev-secrets.ps1 has been run and the database is migrated.
  Optional: -ConnectionString overrides the database used by all services for this run.
#>
param([string]$ConnectionString)

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$envPrefix = '$env:ASPNETCORE_ENVIRONMENT=''Development''; '
if ($ConnectionString) { $envPrefix += "`$env:ConnectionStrings__CustomerFeedbackDb='$ConnectionString'; " }

$services = @(
    @{ Name = 'Identity';  Port = 5101 },
    @{ Name = 'Feedback';  Port = 5102 },
    @{ Name = 'Analytics'; Port = 5103 }
)
foreach ($s in $services) {
    $cmd = "$envPrefix dotnet run --no-launch-profile --project `"$root\src\CustomerFeedback.$($s.Name).Api`" --urls http://localhost:$($s.Port)"
    Start-Process powershell -ArgumentList '-NoExit', '-Command', $cmd
}

Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location `"$root\web`"; npm run dev"
Write-Host 'Started. Open http://localhost:5173'
