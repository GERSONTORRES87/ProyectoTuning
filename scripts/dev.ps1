param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskLocalSdk = Join-Path $env:LOCALAPPDATA 'Programs\dotnet\dotnet.exe'
$taskDotnet = if (Test-Path -LiteralPath $taskLocalSdk) { $taskLocalSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $taskDotnet -Parent
$env:Path = $env:DOTNET_ROOT + ';' + $env:Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not $NoBuild) {
    & $taskDotnet build (Join-Path $taskRoot 'turning.sln') --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'La compilación falló. Revisa los errores anteriores.' }
}

foreach ($taskPort in 5080, 5251) {
    if (Get-NetTCPConnection -State Listen -LocalPort $taskPort -ErrorAction SilentlyContinue) {
        throw "El puerto $taskPort ya está en uso. Revisa la instancia existente antes de iniciar otra."
    }
}

$taskLogs = Join-Path $env:LOCALAPPDATA 'Turning\logs'
New-Item -ItemType Directory -Path $taskLogs -Force | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Api__BaseUrl = 'http://localhost:5080'
$taskApiDll = Join-Path $taskRoot 'src\turning.API\bin\Debug\net10.0\turning.API.dll'
$taskWebDll = Join-Path $taskRoot 'src\turning.Web\bin\Debug\net10.0\turning.Web.dll'
$taskApi = $null
$taskWeb = $null

try {
    $taskApi = Start-Process -FilePath $taskDotnet -ArgumentList @('"' + $taskApiDll + '"', '--urls', 'http://localhost:5080') `
        -WorkingDirectory (Join-Path $taskRoot 'src\turning.API') -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $taskLogs 'api.log') -RedirectStandardError (Join-Path $taskLogs 'api-error.log')

    $taskReady = $false
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $taskDeadline) {
        if ($taskApi.HasExited) { throw "La API terminó. Revisa $taskLogs\api-error.log y api.log. Es necesario SQL Server LocalDB." }
        try {
            $taskHealth = Invoke-RestMethod -Uri 'http://localhost:5080/api/health' -TimeoutSec 2
            if ($taskHealth.isHealthy) { $taskReady = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $taskReady) { throw "La API no estuvo disponible en 60 segundos. Revisa los logs en $taskLogs." }

    $taskWeb = Start-Process -FilePath $taskDotnet -ArgumentList @('"' + $taskWebDll + '"', '--urls', 'http://localhost:5251') `
        -WorkingDirectory (Join-Path $taskRoot 'src\turning.Web') -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $taskLogs 'web.log') -RedirectStandardError (Join-Path $taskLogs 'web-error.log')

    @{ ApiPid = $taskApi.Id; WebPid = $taskWeb.Id } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskLogs 'processes.json') -Encoding utf8
    Write-Output 'API: http://localhost:5080/swagger'
    Write-Output 'Web: http://localhost:5251'
    Write-Output "Logs: $taskLogs"
} catch {
    foreach ($taskStarted in @($taskWeb, $taskApi)) {
        if ($null -ne $taskStarted -and -not $taskStarted.HasExited) { Stop-Process -Id $taskStarted.Id }
    }
    throw
}
