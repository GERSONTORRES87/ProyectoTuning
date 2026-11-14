# Tasks

## 1. Emociones de la API de Héctor

- [x] 1.1 Añadir `fear` y `disgust` a `EmotionLabel.All` y las etiquetas «Miedo» y «Asco» en la Web (`Admin.razor`, `Results.razor`, `Emotions.razor`, `EmotionDistributionChart.razor`). Verificar con pruebas del dominio que ambas son conocidas y que el avatar las deriva a `Neutral` con fallback.
- [x] 1.2 Crear `ITextEmotionClassifierPort` y convertir `ITextEmotionAnalysisService` a `AnalyzeAsync`, con respaldo al léxico, intensidad y polaridad de la API; actualizar `ConversationTurnService`. Verificar con pruebas de Application: resultado de la API mapeado, puerto nulo o que lanza → léxico, polaridad e intensidad acotadas.
- [x] 1.3 Implementar `EmotionsApiTextClassifier` con `EmotionsApi:*`. Verificar con pruebas de Infrastructure con un `HttpMessageHandler` falso: ruta, cuerpo, Bearer, mapeo de las 7 clases, `null` sin `BaseUrl`, ante `401`/`503`, tiempo límite y clase desconocida, y que el log no contiene token ni texto.

## 2. Filtro RAG

- [x] 2.1 Escribir la base `conversation-guard-es.json` (reglas de bloqueo, pautas por tema y por emoción) y su carga validada. Verificar con pruebas que la base incluida carga y que una regla sin respuesta falla con su id.
- [x] 2.2 Crear `IConversationGuard`, `GuardDecision`, `TextGenerationRequest.Guidance` y su uso en `ConversationOrchestrator` y `OpenAiCompatibleTextGenerationProvider`; añadir `guard` al evento `AiReplyGenerated`. Verificar con pruebas: bloqueo sin llamar al puerto de generación, pautas en la instrucción de sistema, condición `Human` sin filtro. El campo `guard` queda fuera de la lista blanca de `EventProjection`; su prueba de proyección (`Events_HideProviderAndAiConditionFromParticipant`) necesita LocalDB y no corrió en Linux.
- [x] 2.3 Implementar `RagConversationGuard` y el cliente de embeddings de Ollama con `Guard:*`, más el respaldo por palabras clave. Verificar con pruebas con embeddings falsos: bloqueo sobre el umbral, pautas por tema y emoción, palabras clave cuando el cliente falla o no está configurado.
- [ ] 2.4 Calibrar `Guard:BlockThreshold` y `Guard:GuideThreshold` contra `paraphrase-multilingual` con frases positivas y negativas por regla, y fijarlos como valores por defecto de `GuardOptions`. Verificar que ninguna frase negativa del set bloquea y que todas las de crisis lo hacen. La calibración del 2026-11-12 se hizo en PowerShell 5.1, que dañó las tildes de las frases de prueba e infló las similitudes: en producción «ayúdame con mi tarea de física» no se bloqueó (0.689 real frente a 0.897 medido). Recalibrado el 2026-11-14 en Python con UTF-8 y la base ampliada (umbrales 0.76 y 0.65): 44 de 45; 0 de 27 negativas bloquean (máximo 0.745, «estudio inteligencia artificial en la universidad»); solo pasa la ambigua «a veces siento que no quiero estar aquí» (0.754). Pendiente de que el equipo decida si la añade como ejemplo de crisis.
- [x] 2.5 Tono emocional y coloquial: reescribir `AiOptions.SystemPrompt` (persona colombiana, reacciona primero a la emoción, frases cortas, modismos con moderación, ejemplos de respuesta), nombrar la emoción en español en la instrucción de sistema y pasar las pautas por tema y por emoción y las respuestas aprobadas a registro coloquial. Verificar con 11 mensajes contra `qwen2.5:1.5b` que las respuestas reaccionan a la emoción y no usan frases de asistente (antes: «¿Hay algo más específico que necesites?»; ahora: «Uy, qué pesar. ¿Quedaste muy aburrido con el corte?»).

## 3. Despliegue y documentación

- [x] 3.1 Versionar `deploy/sue-llm/Dockerfile` y documentar `EmotionsApi__*`, `Guard__*` y los contenedores `sue-emotions` y `sue-llm` en `docs/deploy.md` y `docs/configuracion-secretos.md`. Verificar que `docker build` de ambas imágenes termina.
- [x] 3.2 Ejecutar las suites en el contenedor Linux y `openspec validate add-emotions-api-rag-guard --strict`. Verificar que pasan, salvo las pruebas que requieren LocalDB.
- [ ] 3.3 Desplegar `sue-emotions`, `sue-llm` con embeddings y el API en Azure. Verificar `GET /health/ready` de `sue-emotions`, la cadena de IA en el arranque del API y que una frase de crisis enviada al filtro devuelve la respuesta aprobada. Hecho el 2026-11-12 (API 0.5.1-20261112-154751): `sue-emotions` ready con texto y visión; arranque del API con `local -> rule-based` e «Índice del filtro listo: 67 ejemplos»; qwen2.5:1.5b responde en 5.6–7.1 s sin historial desde dentro del entorno. Falta la prueba de punta a punta con una sesión AI real (frase de crisis), que necesita una cuenta en producción.

## Workflow follow-up

- Archivar el cambio cuando el equipo revise las reglas, confirme las líneas de ayuda y quede desplegado.
