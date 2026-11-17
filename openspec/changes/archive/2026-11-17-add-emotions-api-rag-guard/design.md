# Design

## Context

- `ConversationTurnService` llama a `ITextEmotionAnalysisService.Analyze` (síncrono, léxico del dominio `TextEmotionAnalyzer`) sobre el turno del participante, persiste la lectura y pasa un `EmotionContext` a `ConversationOrchestrator`, que construye la `TextGenerationRequest` y llama a `ITextGenerationPort`. Es el único llamador del análisis de texto.
- La API de Héctor (`services/emotions-api`, FastAPI) expone `POST /api/v1/emotions/text` con Bearer, devuelve `emotion` (7 clases en mayúsculas), `confidence`, `probabilities`, `modelVersions.text` y limita a 60 peticiones por minuto e IP (configurable con `RATE_LIMIT_PER_MINUTE`). Descarga los modelos de Hugging Face al arrancar.
- `EventProjection.ToDto` filtra el payload del participante con una lista blanca: un campo nuevo en `AiReplyGenerated` queda oculto sin tocarla.
- `sue-llm` (Ollama) ya sirve `qwen2.5:1.5b`; Ollama también sirve embeddings en `POST /api/embed`.

## Goals / Non-Goals

**Goals:**
- Emoción real del texto sin que una caída de la API cueste el turno.
- Filtro que añada como máximo ~300 ms al turno y nunca detenga la conversación.
- Base de conocimiento editable por el equipo sin tocar código.

**Non-Goals:**
- Usar imagen, video o cámara de la API de Héctor.
- Modificar el código de `services/emotions-api`.
- Una base vectorial externa: con ~25 entradas basta un índice en memoria.
- Editar la base de conocimiento desde la Web.

## Decisions

### 1. Puerto de clasificación y servicio asíncrono
`ITextEmotionAnalysisService.Analyze` pasa a `AnalyzeAsync(text, ct)`. El servicio prueba primero un puerto nuevo `ITextEmotionClassifierPort.ClassifyAsync` (Application/Interfaces) y, si devuelve `null` o lanza, usa el léxico como hoy. Así la política de respaldo sigue en Application y el HTTP queda en Infrastructure.
Intensidad = `1 − P(neutral)`; polaridad = `P(joy) − (P(anger)+P(sadness)+P(fear)+P(disgust))`, acotada a [−1, 1]; `surprise` cuenta como neutra, igual que en la valencia de la API.

### 2. Cliente `EmotionsApiTextClassifier`
`HttpClient` tipado con `EmotionsApi:BaseUrl`, `EmotionsApi:Token` y `EmotionsApi:TimeoutMs` (1500). Sin `BaseUrl` devuelve `null` sin red. Cualquier estado no exitoso, JSON ilegible o clase desconocida devuelve `null` y se registra sin texto ni token.

### 3. Vocabulario
`EmotionLabel` añade `Fear = "fear"` y `Disgust = "disgust"` a `All`. El avatar no cambia: su fallback a `Neutral` ya cubre las dos. La Web traduce «Miedo» y «Asco».

### 4. Guardia RAG en Application con índice en Infrastructure
Puerto `IConversationGuard.EvaluateAsync(message, emotion, ct)` → `GuardDecision { Mode, RuleId, Response, Guidance }`. `ConversationOrchestrator` lo llama solo en condición `AI` y para turnos del participante:
- `Blocked` → devuelve el resultado con texto aprobado, proveedor `guard`, modelo = id de la regla, sin degradación y sin llamar a `ITextGenerationPort`.
- Si no → `TextGenerationRequest.Guidance` lleva las pautas; `OpenAiCompatibleTextGenerationProvider` las añade a la instrucción de sistema. `rule-based` las ignora.
- El evento `AiReplyGenerated` añade `guard` (`blocked:<id>`, `context:<ids>`, `keywords`, `off`).

### 5. Recuperación
`RagConversationGuard` (singleton) carga `conversation-guard-es.json` como recurso embebido, igual que `emotion-lexicon-es.json`. En el primer uso pide a Ollama los embeddings de todos los ejemplos en una sola llamada y los guarda en memoria. Por turno embebe el mensaje (una llamada) y calcula la similitud coseno máxima contra los ejemplos de cada entrada. Umbrales configurables: `Guard:BlockThreshold` y `Guard:GuideThreshold`, calibrados con el modelo real y fijados en `appsettings.json`.
*Alternativa descartada:* que el propio LLM clasifique el mensaje. Duplicaría la latencia del modelo en cada turno.

### 6. Respaldo por palabras clave
Sin `Guard:EmbeddingsBaseUrl`, o si Ollama falla o pasa de `Guard:TimeoutMs` (800), las reglas de bloqueo se evalúan por palabras clave sobre el texto normalizado (minúsculas, sin tildes). Así la regla de crisis nunca depende de un servicio de red.

### 7. Despliegue
- `sue-emotions`: imagen construida desde `services/emotions-api` sin cambios, 2 vCPU / 4 GiB, ingreso interno puerto 8000, `API_TOKEN` como secreto generado y compartido con el API de SUE (`EmotionsApi__Token`), `RATE_LIMIT_PER_MINUTE=600`.
- `sue-llm`: misma imagen de Ollama con `paraphrase-multilingual` añadido; el `Dockerfile` se versiona en `deploy/sue-llm/`.

## Risks / Trade-offs

- [La API de Héctor tarda en descargar sus modelos al arrancar y responde `503` mientras tanto] → el respaldo local cubre ese tramo; se verifica `GET /health/ready` tras desplegar.
- [Umbral de bloqueo demasiado bajo corta conversaciones legítimas] → umbral calibrado con ejemplos positivos y negativos antes de fijarlo; cada bloqueo queda trazado con su regla para revisarlo.
- [Respuesta aprobada a «¿eres una IA?» puede delatar la condición] → texto neutro y editable por el equipo; la decisión metodológica es del equipo de investigación.
- [Números de las líneas de ayuda] → la semilla usa líneas de Colombia y el equipo debe confirmarlas antes del ensayo.
- [Latencia añadida al turno: clasificación ~100–300 ms + embedding ~50–150 ms] → tiempos límite de 1500 ms y 800 ms; el filtro corre después de guardar el turno del participante, igual que la generación.
