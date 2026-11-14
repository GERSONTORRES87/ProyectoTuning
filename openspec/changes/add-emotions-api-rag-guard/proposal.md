# Proposal

## Why

La emoción de cada mensaje del participante sale hoy de un léxico simulado, aunque el equipo ya tiene un clasificador real en español: la API de emociones multimodal de Héctor Restrepo (`services/emotions-api`). Además, todo mensaje de la condición `AI` llega tal cual al modelo de lenguaje, sin un filtro que detecte señales de crisis, intentos de desenmascarar al interlocutor o pedidos fuera del experimento, y sin contexto que oriente la respuesta.

## What Changes

- SUE clasifica la emoción del texto del participante con la API de emociones (`POST /api/v1/emotions/text`); si la API no está configurada o falla, usa el léxico actual.
- El vocabulario de emociones de SUE se amplía con `fear` y `disgust`, que devuelve la API. El avatar las muestra como `Neutral`, como ya hace con `confusion`.
- Nuevo filtro RAG antes del modelo de lenguaje en la condición `AI`: busca el mensaje en una base de conocimiento corta y versionada.
  - Si coincide con una regla de bloqueo, responde un texto aprobado sin llamar al modelo.
  - Si no, añade al prompt las pautas más parecidas y la pauta de la emoción detectada.
- El interlocutor de la condición `AI` responde en tono emocional y coloquial colombiano: reacciona primero a lo que la persona siente, en frases cortas, con modismos usados con moderación; las pautas y las respuestas aprobadas siguen ese registro.
- La búsqueda usa embeddings multilingües servidos por el contenedor local `sue-llm` (Ollama, `paraphrase-multilingual`). Sin embeddings, las reglas de bloqueo se comprueban por palabras clave.
- El evento de la respuesta registra qué hizo el filtro. El participante no lo ve.
- Despliegue: nuevo contenedor interno `sue-emotions` con la API de Héctor sin modificar su código.

## Capabilities

### New Capabilities
- `text-emotion-analysis`: clasificación emocional del texto del participante, vocabulario de emociones y respaldo cuando el clasificador externo falla.
- `conversation-guard`: filtro de los mensajes de la condición `AI` antes del modelo de lenguaje: bloqueo con respuesta aprobada, contexto recuperado y trazabilidad.

### Modified Capabilities
<!-- avatar-expression no cambia: fear y disgust caen en su fallback a Neutral ya especificado. -->

## Impact

- **Domain:** `EmotionLabel` (+`fear`, +`disgust`).
- **Application:** `ITextEmotionAnalysisService` pasa a ser asíncrono; nuevo puerto `ITextEmotionClassifierPort`; nuevo puerto `IConversationGuard`; `ConversationOrchestrator` y `TextGenerationRequest` (pautas).
- **Infrastructure:** cliente de la API de emociones, guardia RAG con su base de conocimiento embebida (`conversation-guard-es.json`) y cliente de embeddings de Ollama.
- **API:** configuración `EmotionsApi:*` y `Guard:*`; el evento `AiReplyGenerated` añade `guard`, oculto al participante.
- **Web:** etiquetas «Miedo» y «Asco» en resultados, emociones y administración.
- **Despliegue:** `sue-emotions` (API de Héctor, token Bearer como secreto compartido) y `sue-llm` con el modelo de embeddings.
