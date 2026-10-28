[CmdletBinding()]
param(
    [Uri]$BaseUrl = 'http://localhost:5251/'
)

$ErrorActionPreference = 'Stop'

foreach ($route in @('login', 'workspace', 'admin', 'interlocutor')) {
    $response = Invoke-WebRequest -Uri ([Uri]::new($BaseUrl, $route)) -UseBasicParsing -TimeoutSec 15
    $html = [string]$response.Content
    if ($response.StatusCode -ne 200 -or [string]::IsNullOrWhiteSpace($html) -or $html -notmatch '_framework/blazor\.web\.js') {
        throw "La ruta /$route no entrega el shell interactivo; el usuario puede quedar ante una pagina vacia."
    }

    Write-Output "OK /$route entrega el shell interactivo."
}
