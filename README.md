# Turning / SUE

Experimento de conversación Human/AI con sesiones temporizadas, emociones, avatar, encuesta y resultados persistidos en SQL Server. La Web Blazor consume exclusivamente la API.

## Requisitos

- .NET SDK 10.
- SQL Server 2022 LocalDB (instancia `MSSQLLocalDB`) o un SQL Server configurado mediante `ConnectionStrings__DefaultConnection`.
- PowerShell 7 para los scripts.

SQLite no se usa. Las pruebas de persistencia necesitan un SQL Server real.

## Ejecutar la aplicación

Desde la raíz del proyecto:

```powershell
./scripts/dev.ps1
```

El script compila y levanta ambos procesos en Development:

- Web: http://localhost:5251
- API y Swagger: http://localhost:5080/swagger
- Logs y PID de los procesos: `$env:LOCALAPPDATA\Turning\logs`

El script reconoce el SDK instalado en `$env:LOCALAPPDATA\Programs\dotnet` y, si no existe, utiliza el `dotnet` del PATH. Para reutilizar una compilación ya comprobada: `./scripts/dev.ps1 -NoBuild`.

La API aplica las migraciones existentes al arrancar e inicializa cuatro preguntas de encuesta. No carga conversaciones sintéticas automáticamente.

## Cuentas de demostración

Sólo en **Development** se crean estas cuentas:

| Rol | Correo | Contraseña local |
|---|---|---|
| Administrator | admin@turning.local | TurningDemo2026! |
| Researcher | researcher@turning.local | TurningDemo2026! |

Los participantes se registran en la Web. El registro público siempre concede `Participant`. El administrador puede cambiar un rol desde Configuración usando el identificador que aparece en el panel del usuario; ese usuario debe iniciar sesión de nuevo.

## Recorrido

1. Registrar un participante y abrir Sesiones.
2. Crear y abrir una sesión; el backend asigna Human o AI sin revelar la condición al participante.
3. El propietario pulsa Iniciar. Para Human, otra cuenta se une desde Sesiones con el código de invitación y abre esa misma conversación.
4. Enviar mensajes. La conversación consulta mensajes nuevos automáticamente y conserva las identidades de ambos emisores.
5. El propietario completa la sesión, o ésta termina por duración/inactividad. El interlocutor y los observadores no pueden completarla en su nombre.
6. Contestar la encuesta. Un único envío guarda las respuestas definitivamente.
7. Consultar resultados. Researcher y Administrator tienen consulta transversal; Participant sólo accede a su sesión y conserva oculta la condición.

Ver el recorrido detallado en [docs/demo.md](docs/demo.md).

## Validación

```powershell
dotnet build turning.sln
dotnet test turning.sln --no-restore
# Con la API ya iniciada:
./scripts/verify-workflow.ps1
openspec validate --all
```

Si `dotnet` del PATH sólo encuentra los runtimes antiguos, usa el ejecutable del SDK instalado:

```powershell
& "$env:LOCALAPPDATA\Programs\dotnet\dotnet.exe" test turning.sln
```

`verify-workflow.ps1` crea cuentas/sesiones identificadas como verificación y comprueba HTTP Human/AI, concurrencia, roles, encuesta y resultados. Guarda evidencia en `../validacion/workflow`.

## Integraciones y alcance

La generación admite proveedores externos configurados por entorno (`Ai__OpenAi__ApiKey`, `Ai__OpenRouter__ApiKey`); sin claves utiliza el adaptador por reglas existente. El análisis emocional de texto es local; audio/video continúan simulados. El avatar muestra la expresión derivada; el build Unity no está integrado. Las preferencias sin endpoint se identifican como no disponibles.

La coordinación de mensajes está prevista para una instancia de API. Las especificaciones vigentes están en `openspec/specs`; el cambio de este recorrido está en `openspec/changes/complete-experiment-workflow`. Las specs de `specs/legacy` son históricas.
