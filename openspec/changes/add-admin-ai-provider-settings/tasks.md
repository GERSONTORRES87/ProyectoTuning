# Tasks

## 1. Proveedor local

- [ ] 1.1 Añadir `AiOptions.Local` (`Name = "local"`), `AiProviderOptions.RequiresApiKey` (falso solo en `Local`) y el orden por defecto `["local", "openai", "openrouter", "rule-based"]`; incluir `Local` en `Validate`. Verificar con pruebas de `AiOptionsTests`: `local` sin clave y con URL y modelo es utilizable; sin `BaseUrl` no lo es; `openrouter` con marcador sigue sin serlo.
- [ ] 1.2 Registrar en `AddAiTextGeneration` los tres proveedores HTTP aunque no tengan clave (sección `Local` incluida), conservando el aviso de clave de marcador y el registro de la cadena. Verificar con una prueba del API que el contenedor de servicios resuelve tres proveedores HTTP más `rule-based` y que el log de arranque sigue listando solo los utilizables.
- [ ] 1.3 Versionar `deploy/sue-llm/Dockerfile` (Ollama 0.40.12 con `qwen2.5:1.5b`) y documentar en `docs/deploy.md` y `docs/configuracion-secretos.md` las variables `Ai__Local__*`. Verificar que `docker build` de `deploy/sue-llm` termina y que `ollama list` muestra el modelo.

## 2. Selección persistida

- [ ] 2.1 Crear la entidad `AiProviderSetting` y su configuración en `TurningDbContext`, y generar la migración con `dotnet ef migrations add AddAiProviderSetting` (en el contenedor `mcr.microsoft.com/dotnet/sdk:10.0` si `dotnet-ef` no está instalado). Verificar que la solución compila y que `has-pending-model-changes` no reporta cambios.
- [ ] 2.2 Añadir el repositorio de la selección (leer la fila única, crear o actualizar con `RowVersion`). Verificar con una prueba de Infrastructure contra LocalDB que guardar dos veces deja una sola fila con el último valor.

## 3. Servicio y enrutado por turno

- [ ] 3.1 Crear `IAiProviderSettingsService` con `GetAsync` y `UpdateAsync(actorId, provider, model)`, `AiProviderSelectionCache` (singleton) y el puerto `IAiProviderHealth`. Validar rol `Administrator` (`AUTH_FORBIDDEN`), proveedor conocido y modelo (1–200 caracteres, `[A-Za-z0-9._:/-]`) con `AI_SETTINGS_INVALID`, y disponibilidad con `AI_PROVIDER_UNAVAILABLE`. Verificar con pruebas de Application: no admin rechazado, proveedor desconocido, modelo vacío/largo/con espacios, proveedor sin clave, cambio válido que actualiza la caché y llama a `Reset`.
- [ ] 3.2 Añadir `TextGenerationRequest.Model` opcional y usarlo en `OpenAiCompatibleTextGenerationProvider` cuando viene informado. Verificar con una prueba que el cuerpo enviado lleva el modelo del turno y, sin él, el de la configuración.
- [ ] 3.3 Hacer que `TextGenerationRouter` use la selección de la caché: `[seleccionado, rule-based]`, solo `rule-based`, o `Ai:ProviderOrder` sin selección. Verificar con pruebas del router: responde el seleccionado con su modelo; si falla responde `rule-based` degradado; selección `rule-based` no llama a ningún proveedor HTTP; sin selección se respeta el orden configurado.
- [ ] 3.4 Implementar `IAiProviderHealth` sobre `ProviderCircuitBreaker.Reset` y registrar servicio, caché y puerto en la inyección de dependencias. Verificar con una prueba que un proveedor deshabilitado por cuota vuelve a intentarse después de guardar la selección.

## 4. API

- [ ] 4.1 Crear `AdminAiSettingsController` (`GET` y `PUT` en `api/admin/ai-settings`, `[Authorize(Roles = "Administrator")]`) y mapear `AI_SETTINGS_INVALID` → 400 y `AI_PROVIDER_UNAVAILABLE` → 422 en el contrato de errores. Verificar con pruebas del API: 403 para `Researcher` y `Participant`, 200 para `Administrator`, 400/422 con su código, y que la respuesta no contiene la clave configurada.
- [ ] 4.2 Documentar los dos endpoints en `docs/3-api-e2e/endpoints-principales.md` y añadirlos a la colección Postman. Verificar que la colección sigue siendo JSON válido.

## 5. Web

- [ ] 5.1 Añadir el cliente (`GetAiSettingsAsync`, `UpdateAiSettingsAsync`) y sus DTO. Verificar en `ApiClientTests` ruta, verbo, cuerpo sin clave y cabecera de autorización.
- [ ] 5.2 Añadir la tarjeta «Inteligencia artificial» en `Admin.razor` dentro de `AuthorizeView Roles="Administrator"`, con proveedores no disponibles deshabilitados, modelo prellenado, aviso de que afecta a sesiones en curso, resultado y error. Verificar con una prueba de componente que el `Administrator` ve la tarjeta y guarda, y que el `Researcher` no la ve.

## 6. Verificación

- [ ] 6.1 Ejecutar las suites en el contenedor Linux y `openspec validate add-admin-ai-provider-settings --strict`; verificar que pasan, salvo las pruebas que requieren LocalDB.
- [ ] 6.2 Desplegar en Azure (API con `Ai__Local__*` en lugar de las variables provisionales `Ai__OpenAi__*`, Web) y verificar desde `/admin` que cambiar a `rule-based` y de vuelta a `local` se refleja en el proveedor de los turnos siguientes.

## Workflow follow-up

- Archivar el cambio cuando el equipo lo revise y quede desplegado.
