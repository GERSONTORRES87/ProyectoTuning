# Paquete de despliegue de SUE (preparación, sin ejecutar)

Este documento prepara la actualización de Azure; **no despliega nada**. Ningún comando de
la sección "Comandos" se ha ejecutado, y cada uno requiere autorización explícita del equipo.

## Estado verificado en local

- `dotnet build turning.sln -c Release` y las suites de pruebas pasan (ver `AI_HANDOFF.md`).
- `dotnet publish -c Release` funciona para `turning.API` y `turning.Web`.
- **Las imágenes no se han construido**: Docker Desktop no estaba en ejecución. El
  `src/turning.API/Dockerfile` es nuevo y no se ha probado con `docker build`; el de
  `src/turning.Web` ya existía.

## Imágenes

| Servicio | Imagen actual en Azure | Propuesta | Contexto de build |
|---|---|---|---|
| sue-back-end | `sue001.azurecr.io/sue-back-end:0.1.1` | `0.2.0` | raíz del repositorio (`src/turning.API/Dockerfile`) |
| sue-front-end | `sue001.azurecr.io/sue-front-end:0.1.2` | `0.2.0` | `src/turning.Web` |

Ambos contenedores escuchan en el puerto **8080** (coincide con el ingreso de Azure).
Se publican las dos imágenes: el backend corrige permisos, eventos, avatar y configuración de IA, y el
frontend cambia sus pantallas.

## Configuración (solo nombres; los valores no van en el repositorio ni en la imagen)

| Servicio | Variable | Tipo |
|---|---|---|
| API | `ASPNETCORE_ENVIRONMENT=Production` | valor |
| API | `ConnectionStrings__DefaultConnection` | secreto |
| API | `Jwt__SigningKey` (32 caracteres o más) | secreto |
| API | `Session__DurationSeconds`, `Session__InactivitySeconds` | valor, opcional |
| API | `Ai__OpenAi__ApiKey`, `Ai__OpenRouter__ApiKey` | secreto, opcional |
| API | `Ai__ProviderOrder__0`, `Ai__ProviderOrder__1`... | valor, opcional (debe terminar en `rule-based`) |
| API | `PasswordReset__BaseUrl` (URL pública de la Web) | valor |
| API | `Email__From`, `Email__Smtp__Username` (cuenta Gmail dedicada a SUE, nunca `proinvsemillero@gmail.com`) | valor |
| API | `Email__Smtp__Host=smtp.gmail.com`, `Email__Smtp__Port=587` | valor |
| API | `Email__Smtp__Password` (contraseña de aplicación de Gmail) | secreto |
| Web | `Api__BaseUrl` (hostname interno del backend) | valor |

Más detalle en `docs/configuracion-secretos.md`. Sin claves de IA el sistema arranca igual y
responde con el adaptador por reglas, marcado como degradado. Sin la cuenta de correo también
arranca, con una advertencia, pero el restablecimiento de contraseña no entrega ningún correo.

## Compuertas antes de desplegar

1. Build y todas las pruebas en verde, y la aceptación HTTP/navegador del handoff completada.
2. `dotnet ef migrations has-pending-model-changes -p src/turning.Infrastructure -s src/turning.API`
   sin cambios pendientes (el worktree no añade migraciones). `dotnet-ef` no está instalado en
   este equipo, así que no se ejecutó; evidencia equivalente: EF Core 10 hace fallar `Migrate()` si el
   modelo difiere del snapshot, y el API arrancó en Development contra LocalDB.
3. `dotnet ef migrations script --idempotent -p src/turning.Infrastructure -s src/turning.API -o sueDb.sql`,
   revisado por una persona. **El API ejecuta `Database.Migrate()` en cada arranque, en cualquier
   entorno**: la primera vez que arranque la imagen nueva aplicará sobre `SueDb` todo lo que falte.
   No se ha leído `__EFMigrationsHistory` de Azure (sin autorización), así que no se sabe si falta algo.
4. Sondas del backend en `/api/health`.
5. Decisión del equipo sobre réplicas mínimas: con 0 réplicas la primera visita tardó más de 28 s.

## Comandos (NO EJECUTADOS)

```
# Construcción en el registro (backend primero, luego frontend)
az acr build -r sue001 -t sue-back-end:0.2.0 -f src/turning.API/Dockerfile .
az acr build -r sue001 -t sue-front-end:0.2.0 -f Dockerfile src/turning.Web

# Actualización de los contenedores
az containerapp update -n sue-back-end  -g "SEMILLERO(PROINV2025-08)" --image sue001.azurecr.io/sue-back-end:0.2.0
az containerapp update -n sue-front-end -g "SEMILLERO(PROINV2025-08)" --image sue001.azurecr.io/sue-front-end:0.2.0
```

Reversión: repetir `az containerapp update` con `0.1.1` (backend) y `0.1.2` (frontend); las
etiquetas antiguas se conservan. **Una migración ya aplicada no se revierte con la imagen.**

## Aceptación posterior en Azure

Repetir los casos de `validacion/azure/informe.md` sobre la versión nueva: preguntas y respuestas
reales de la encuesta, persistencia de emociones desde la Web, actualización automática del chat
Human, condición y proveedor ocultos al participante (también en eventos y resultados), eventos de cierre,
avatar y resultados con datos guardados. Probar Researcher y Administrator con cuentas de prueba
dedicadas, y medir carga con un objetivo de usuarios definido.

## Aceptación de un proveedor de IA real (pendiente de credenciales)

El adaptador por reglas es el respaldo intencional y su uso **no prueba** que un modelo externo
funcione. Para afirmarlo hace falta, con una clave real cargada por el equipo como secreto:

1. Una sesión AI con 5 turnos o más.
2. En la vista de Researcher/Administrator de los resultados: `provider` distinto de `rule-based` y
   `degraded=false` en las respuestas.
3. Con una clave inválida a propósito: el turno sigue respondiendo con `rule-based` marcado como degradado.
4. El participante no ve `provider`, `model` ni la condición en ningún caso.

## Límites conocidos

- Una sola instancia del API: la coordinación por sesión (`SessionOperationLock`) vive en memoria.
- El planificador espera por turnos el candado de cada sesión; una generación de IA en curso
  (hasta `Ai:TotalBudgetMs`, 10 s) puede retrasar el cierre de otras sesiones.
- Audio y video son simulados y la animación Unity no está integrada.
