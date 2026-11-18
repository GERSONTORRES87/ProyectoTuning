# Smoke test

Prueba rápida para comprobar que el proyecto está sano antes de una entrega: compila, pasan las pruebas automáticas, la base de datos tiene todas las migraciones y la API arranca y responde. El script es [scripts/smoke.ps1](../../scripts/smoke.ps1).

Probado en Windows 11 con PowerShell 5.1, .NET 10 y SQL Server LocalDB el 17 de noviembre de 2026.

## Antes de correrlo

- .NET SDK 10 y la herramienta `dotnet ef` (`dotnet ef --version` debe responder).
- SQL Server LocalDB (`sqllocaldb info` debe mostrar `MSSQLLocalDB`).
- Que la API no esté corriendo en el puerto 5080.
- Internet solo para el primer paso (`dotnet restore`); lo demás funciona sin conexión.

## Cómo correrlo

Desde la raíz del repositorio, en PowerShell normal (no hace falta abrirlo como administrador). Pegar cada bloque por separado y esperar a que termine antes de pegar el siguiente.

**1. Restaurar los paquetes.** El script compila con `--no-restore`, así que no los descarga por su cuenta:

```powershell
dotnet restore
```

**2. Crear una clave JWT temporal.** El paso de migraciones arranca la aplicación en modo `Production`, y en ese modo exige la clave. Las user-secrets no sirven aquí porque solo se cargan en `Development`. Esta forma funciona en PowerShell 5.1 y en 7:

```powershell
$b = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
$env:Jwt__SigningKey = [Convert]::ToBase64String($b)
"Largo de la clave: $($env:Jwt__SigningKey.Length)"
```

Debe mostrar `Largo de la clave: 64`. La clave solo existe mientras esa ventana esté abierta y no se guarda en ningún archivo.

**3. Correr el smoke test:**

```powershell
./scripts/smoke.ps1
```

Tarda entre 3 y 6 minutos.

## Qué hace y cómo leer el resultado

El script ejecuta cuatro pasos en orden y se detiene en el primero que falle:

| Paso | Qué comprueba | Cómo se ve si pasa |
|---|---|---|
| 1. `dotnet build` | Que toda la solución compila | `OK: dotnet build turning.sln` |
| 2. `dotnet test` | Que pasan todas las pruebas automáticas | `Resumen de pruebas: ... con errores: 0` y `OK: dotnet test turning.sln` |
| 3. `dotnet ef database update` | Que la base `Turning` de LocalDB tiene todas las migraciones de base de datos (si ya las tiene, no cambia nada) | Termina sin `FALLO` |
| 4. Health check | Que la API arranca y `GET /api/health` responde | `OK: health check verde (http://localhost:5080/api/health)` |

Si todo pasa, la última línea en verde es:

```
SMOKE TEST COMPLETO: build + test + migracion + health OK
```

Si algo falla, sale en rojo `FALLO: <paso>` y el script termina con código 1. Las causas más comunes están en [troubleshooting.md](troubleshooting.md).

Durante la corrida salen muchas advertencias en amarillo (`CS1591`, `NU1902`, `NU1903`). No son errores y no detienen el script; están explicadas en [troubleshooting.md](troubleshooting.md).

## Observaciones para quien mantiene el script

- El comentario inicial dice que el script "restaura" la solución y que corre las pruebas de Domain, Application e Infrastructure. En realidad no restaura (usa `--no-restore`) y corre las cinco suites de pruebas, incluidas API y Web.
- El paso 3 aplica las migraciones a la base `Turning`, pero la API del paso 4 se conecta a `TurningDev`: `dotnet run` toma el perfil de `launchSettings.json`, que fija el entorno en `Development`. Por eso el health check no comprueba la misma base que se migró en el paso 3.
