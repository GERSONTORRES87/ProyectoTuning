# Proposal

## Why

Hoy el proveedor y el modelo que responden en la condición `AI` se fijan en la configuración del API y solo cambian con un redespliegue o un reinicio. Cuando el modelo gratuito de OpenRouter desapareció (`nvidia/nemotron-3-super-120b-a12b:free`) o faltó la clave, la plataforma quedó respondiendo con el adaptador por reglas sin que nadie del equipo pudiera corregirlo desde la aplicación. El equipo necesita que un Administrator elija quién responde y con qué modelo, y que el cambio aplique en el siguiente turno.

## What Changes

- Nuevo proveedor **local** compatible con OpenAI (Ollama con `qwen2.5:1.5b`, desplegado como contenedor interno `sue-llm`) que no necesita clave de API, junto a OpenAI y OpenRouter.
- Nueva configuración persistida de IA: proveedor activo (`local`, `openrouter`, `openai` o `rule-based`) y modelo, con quién la cambió y cuándo.
- `GET /api/admin/ai-settings` y `PUT /api/admin/ai-settings`, solo para `Administrator`: muestran la selección y la disponibilidad de cada proveedor, y la cambian. Nunca devuelven ni aceptan claves de API.
- La cadena de generación lee la selección vigente en cada turno: proveedor elegido y, si falla, el adaptador por reglas marcado como degradado. Sin selección guardada se mantiene el orden de la configuración actual.
- Guardar la selección rehabilita el proveedor elegido si el cortacircuitos lo había sacado de la cadena.
- Tarjeta «Inteligencia artificial» en Administración (`/admin`), visible solo para `Administrator`.
- Las claves de API siguen como secretos del entorno (Azure / user-secrets); no se guardan en la base ni se editan desde la Web.

## Capabilities

### New Capabilities
- `ai-text-generation`: proveedores de generación de texto para la condición `AI`, cadena de respaldo hasta el adaptador por reglas y la selección de proveedor y modelo que administra el `Administrator`.

### Modified Capabilities
<!-- conversation-turns no cambia: sigue pidiendo la respuesta a ITextGenerationPort; quién la produce lo define ai-text-generation. -->

## Impact

- **Application:** `AiOptions` (proveedor `Local` sin clave obligatoria), nuevo servicio de configuración de IA y su contrato.
- **Infrastructure:** entidad y migración EF para la selección, `TextGenerationRouter` y `OpenAiCompatibleTextGenerationProvider` (modelo por turno), `ProviderCircuitBreaker` (rehabilitar).
- **API:** `AiConfigurationExtensions` (registro del proveedor local), nuevo controlador `api/admin/ai-settings`, códigos de error nuevos.
- **Web:** `AuthApiClient` o cliente nuevo, tarjeta en `Admin.razor`.
- **Despliegue:** contenedor `sue-llm` (Ollama) en `sue-env`, variables `Ai__Local__*` en el API, `docs/deploy.md` y `docs/configuracion-secretos.md`.
- **Base de datos:** una tabla nueva de una fila; la migración se aplica sola al arrancar el API.
