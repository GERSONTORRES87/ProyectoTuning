# Design

## Context

- `AiConfigurationExtensions.AddAiTextGeneration` arma la cadena una sola vez al arrancar: enlaza `AiOptions` desde `Ai:*`, registra un `OpenAiCompatibleTextGenerationProvider` por proveedor con clave real (`OpenAi`, `OpenRouter`) y cierra con `RuleBasedTextGenerationProvider`. `TextGenerationRouter` (scoped) sigue `Ai:ProviderOrder`.
- Un proveedor sin clave o con marcador (`AiProviderOptions.IsPlaceholderApiKey`) ni siquiera se registra: hoy no hay forma de cambiar la cadena sin reiniciar.
- `ProviderCircuitBreaker` es singleton y ya tiene `Reset(provider)`; `Disable` saca un proveedor hasta reiniciar cuando falla por credenciales o cuota.
- El proveedor y el modelo de cada turno ya se guardan en el evento del turno (`ConversationTurnService`, campos `provider`, `model`, `degraded`). Cambiar de proveedor a mitad de un ensayo queda trazado sin trabajo extra.
- El API corre en una sola instancia (`SessionOperationLock` en memoria), así que una caché en memoria de la selección es coherente.
- Ollama expone `/v1/chat/completions` compatible con OpenAI y no valida la cabecera `Authorization`.

## Goals / Non-Goals

**Goals:**
- Cambiar proveedor y modelo sin reiniciar y sin tocar secretos.
- Reutilizar el proveedor compatible con OpenAI para `local`; nada de un cliente nuevo para Ollama.
- Mantener el comportamiento actual cuando nadie ha guardado una selección.

**Non-Goals:**
- Editar claves de API, URLs base, tiempos límite o la instrucción de sistema desde la Web.
- Listar los modelos que ofrece cada proveedor; el modelo se escribe a mano.
- Varias instancias del API o propagación entre ellas.
- Cambiar los proveedores de análisis emocional.

## Decisions

### 1. Tercer proveedor `local` en `AiOptions`
Se añade `AiOptions.Local` (`Name = "local"`, `RequiresApiKey = false`). `AiProviderOptions.IsUsable` deja de exigir clave cuando `RequiresApiKey` es falso. El orden por defecto queda `["local", "openai", "openrouter", "rule-based"]`; `local` sin `BaseUrl` no está disponible, así que un entorno sin Ollama se comporta como hoy.
*Alternativa descartada:* reutilizar la ranura `OpenAi` apuntando a Ollama, como en el despliegue provisional. Funciona, pero los resultados mostrarían `openai` y obliga a inventar una clave.

### 2. Registrar siempre los proveedores HTTP
`AddAiTextGeneration` registra los tres proveedores HTTP aunque no tengan clave; `IsConfigured` sigue decidiendo si se intentan. Así la disponibilidad se puede consultar y el router no depende de lo que hubo al arrancar. El aviso de arranque por clave de marcador se mantiene.

### 3. Selección persistida en una tabla de una fila
Entidad `AiProviderSetting` (`Id` fijo, `Provider`, `Model`, `UpdatedByUserId`, `UpdatedAtUtc`, `RowVersion`) en `TurningDbContext`, con migración EF. Sin fila = sin selección.
*Alternativa descartada:* guardar en memoria o en un archivo. Se pierde en cada reinicio o despliegue, que es justo el problema que se quiere evitar.

### 4. Servicio de Application con caché en memoria
`IAiProviderSettingsService` (Application) expone `GetAsync` (vista para el admin) y `UpdateAsync(actorId, provider, model)`, valida el rol `Administrator` como `AuthService.ListUsersAsync` y lanza `TurningApplicationException` con `AI_SETTINGS_INVALID` (400) o `AI_PROVIDER_UNAVAILABLE` (422). Un singleton `AiProviderSelectionCache` guarda la selección vigente; se llena de la base en el primer turno y se reemplaza al guardar. El router lo lee en cada turno sin consultar la base.

### 5. El router aplica la selección por turno
Con selección, la cadena del turno es `[seleccionado, rule-based]`; si el seleccionado es `rule-based`, solo `rule-based`. El modelo viaja en un campo nuevo opcional `TextGenerationRequest.Model`, que `OpenAiCompatibleTextGenerationProvider` usa en lugar de `AiProviderOptions.Model` cuando viene informado. Sin selección se usa `Ai:ProviderOrder` como hoy.

### 6. Rehabilitación al guardar
`UpdateAsync` llama a `ProviderCircuitBreaker.Reset(provider)` después de persistir. El cortacircuitos vive en Infrastructure, así que el servicio depende de un puerto pequeño (`IAiProviderHealth.Reset`) implementado sobre el breaker.

### 7. Contrato HTTP
`AdminAiSettingsController` en `api/admin/ai-settings` con `[Authorize(Roles = "Administrator")]`. `GET` devuelve `{ provider, model, updatedAtUtc, updatedBy, providers: [{ id, available, defaultModel }] }`. `PUT` recibe `{ provider, model }`. Los errores siguen `ApiErrorContract`. Ningún DTO tiene campo de clave.

### 8. Web
Tarjeta «Inteligencia artificial» en `Admin.razor` dentro de `<AuthorizeView Roles="Administrator">`, con el mismo patrón del segmento de roles: `select` de proveedor (deshabilita los no disponibles), `input` de modelo prellenado con el modelo por defecto del proveedor elegido, «Guardar», aviso y error.

### 9. Contenedor `sue-llm`
Imagen `ollama/ollama:0.40.12` con `qwen2.5:1.5b` descargado en la construcción, `OLLAMA_KEEP_ALIVE=-1`, 4 vCPU / 8 GiB, ingreso interno en `sue-env`. El API lo usa con `Ai__Local__BaseUrl=https://sue-llm.internal.<dominio>/v1/` y `Ai__Local__Model=qwen2.5:1.5b`. El `Dockerfile` se versiona en `deploy/sue-llm/`.

## Risks / Trade-offs

- [Qwen2.5 1.5B en CPU puede superar el presupuesto de 10 s con historiales largos] → `Ai__Local__RequestTimeoutMs=9000`, una sola tentativa por proveedor y respuestas cortas (`MaxOutputTokens` 150); si falla, responde `rule-based` degradado y queda registrado.
- [Cambiar de proveedor a mitad de un ensayo mezcla condiciones] → cada turno ya guarda proveedor y modelo; la tarjeta avisa que el cambio afecta a las sesiones en curso.
- [Modelo mal escrito: el proveedor responde 404 en cada turno] → se clasifica como fallo permanente, responde `rule-based` y el evento muestra el motivo; el admin lo corrige desde la misma tarjeta.
- [Caché en memoria con varias instancias quedaría desfasada] → `shortcut:` documentado; hoy hay una sola instancia por diseño.
- [Costo del contenedor `sue-llm` siempre encendido, ~90–120 USD/mes del crédito] → decisión aceptada por el usuario; se puede escalar a 0 réplicas si se acepta un primer turno lento.

## Migration Plan

1. Desplegar `sue-llm` (ya creado provisionalmente en la ranura `OpenAi`; al aplicar este cambio se pasa a `Ai__Local__*` y se quitan las variables `Ai__OpenAi__*` provisionales).
2. Publicar la imagen nueva del API: la migración crea la tabla al arrancar; sin fila el comportamiento es el de hoy.
3. Publicar la Web.
4. Reversión: volver a las imágenes anteriores; la tabla nueva no la lee nadie y puede quedarse.
