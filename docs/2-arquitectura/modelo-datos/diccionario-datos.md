# Diccionario de datos — Turning MVP

Complementa a `docs/2-arquitectura/modelo-datos/diagrama-er.md` (diagrama visual) con el detalle de
cada tabla y columna. Entregable GE-03 del plan individual de Gerson Torres (DBA).

Convenciones generales:
- Todas las tablas heredan de `BaseEntity`: `Id` (PK, `uniqueidentifier`), `CreatedAt`,
  `UpdatedAt` (nullable) e `IsDeleted` (borrado lógico, `bit`).
- Los identificadores de relación (`*Id`) son claves foráneas (`FK`).
- `UK` = restricción de unicidad (unique key). `IX` = índice no único.

---

## UserAccounts
Cuenta de usuario autenticable (investigador o administrador) que puede ser dueño
de una o varias sesiones experimentales.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador único del usuario. |
| Email | nvarchar(200) | Correo tal como lo ingresó el usuario. |
| NormalizedEmail | nvarchar(200) (UK) | Correo en mayúsculas, usado para validar unicidad y búsquedas. |
| FullName | nvarchar(200) | Nombre visible del usuario. |
| PasswordHash | nvarchar(1000) | Hash de la contraseña (nunca texto plano). |
| Role | nvarchar(50) | Rol del usuario (`Administrator`, `Researcher`, etc.). |
| LastLoginAt | datetime2 (nullable) | Última vez que inició sesión exitosamente. |

---

## ExperimentSessions
Sesión experimental individual: representa una conversación completa bajo una
condición (Human o AI) asociada a un usuario dueño.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la sesión. |
| OwnerUserId | uniqueidentifier (FK → UserAccounts) | Usuario dueño de la sesión. |
| InterlocutorUserId | uniqueidentifier (FK → UserAccounts, nullable) | Interlocutor humano asignado a la sesión (condición `Human`), si lo hay. |
| InterlocutorJoinedAtUtc | datetime2 (nullable) | Momento en que el interlocutor se incorporó a la sesión. |
| SessionCode | nvarchar(20) (UK) | Código corto legible (`EXP-XXXXXXXX`). |
| Condition | nvarchar(20) | Condición experimental: `Human` o `AI`. |
| Status | nvarchar(30) | Estado del ciclo de vida: `Created`, `Active`, `Completed`, `TimedOut`, `Cancelled`. |
| AvatarState | nvarchar(50) | Estado visual/emocional actual del avatar. |
| LastDetectedEmotion | nvarchar(50) (nullable) | Última emoción detectada en la sesión. |
| ConversationTurnCount | int | Contador de turnos registrados (se incrementa en cada mensaje). |
| EmotionSampleCount | int | Contador de lecturas de emoción capturadas. |
| ActivatedAtUtc | datetime2 (nullable) | Momento en que la sesión pasó a `Active`. |
| ExpiresAtUtc | datetime2 (nullable) | Momento en que la sesión expira si no hay actividad. |
| LastActivityAtUtc | datetime2 (nullable) | Última actividad registrada (mensaje o evento). |
| CompletedAtUtc | datetime2 (nullable) | Momento de finalización normal o por timeout. |
| CancelledAtUtc | datetime2 (nullable) | Momento de cancelación, si aplica. |
| CancellationReason | nvarchar(500) (nullable) | Motivo de la cancelación. |
| RowVersion | rowversion | Control de concurrencia optimista (evita que dos procesos pisen la misma sesión). |

**Restricción:** `SessionCode` es único en toda la tabla.

**Interlocutor:** una sesión admite un solo interlocutor, y el dueño no puede ser su propio interlocutor.

---

## ConditionAssignments
Registro de qué condición (Human/AI) fue asignada a una sesión y por qué
estrategia (aleatoria, balanceada, etc.). Relación 1 a 1 con `ExperimentSessions`.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la asignación. |
| SessionId | uniqueidentifier (FK → ExperimentSessions, UK) | Sesión asignada (única: una sesión solo tiene una asignación). |
| Condition | nvarchar(20) | Condición asignada. |
| Strategy | nvarchar(50) | Estrategia usada para decidir la condición. |
| Reason | nvarchar(500) | Justificación o detalle de la asignación. |

---

## ConversationTurns
Cada mensaje individual dentro de una sesión, en orden secuencial.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador del turno. |
| ExperimentSessionId | uniqueidentifier (FK → ExperimentSessions) | Sesión a la que pertenece. |
| SequenceNumber | int | Orden del mensaje dentro de la conversación (inicia en 1). |
| Sender | nvarchar(20) | Emisor: `Participant` (usuario real) o `Interlocutor` (humano o IA al otro lado). |
| Message | nvarchar(4000) | Contenido del mensaje. |
| OriginatingTurnId | uniqueidentifier (nullable) | Si es una respuesta generada por IA, referencia al turno del participante que la originó. |

**Restricción:** no se puede repetir `SequenceNumber` dentro de la misma sesión.

---

## SessionAuditEntries
Bitácora de cambios de estado de una sesión (auditoría), útil para saber quién
o qué provocó cada transición.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la entrada de auditoría. |
| SessionId | uniqueidentifier (FK → ExperimentSessions) | Sesión auditada. |
| PreviousStatus | nvarchar(30) | Estado anterior. |
| NewStatus | nvarchar(30) | Estado nuevo. |
| ActorType | nvarchar(50) | Tipo de actor que provocó el cambio (sistema, usuario, etc.). |
| ActorId | uniqueidentifier (nullable) | Identificador del actor, si aplica. |
| Reason | nvarchar(500) (nullable) | Motivo del cambio. |
| OccurredAtUtc | datetime2 | Momento exacto del cambio. |
| MetadataJson | nvarchar(2000) (nullable) | Datos adicionales en formato JSON libre. |

---

## EmotionReadings
Lectura de emoción detectada durante la sesión, opcionalmente ligada a un
turno de conversación específico.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la lectura. |
| SessionId | uniqueidentifier (FK → ExperimentSessions) | Sesión a la que pertenece. |
| ConversationTurnId | uniqueidentifier (FK → ConversationTurns, nullable) | Turno asociado, si la lectura ocurrió en un mensaje puntual. |
| Source | nvarchar(30) | Origen de la detección (cámara, texto, etc.). |
| Emotion | nvarchar(50) | Emoción detectada (alegría, tristeza, neutral, etc.). |
| Score | float | Intensidad de la emoción (0 a 1); es el valor que usa el avatar. No confundir con `Confidence`. |
| CapturedAtUtc | datetime2 | Momento de la captura. |
| Provider | nvarchar(100) | Proveedor/servicio que generó la lectura. |
| IsDegraded | bit | Indica si la lectura se generó en modo degradado (fallback, sin proveedor real). |
| Confidence | float | Qué tan segura es la detección (0 a 1), independiente de la intensidad. |
| Polarity | float (nullable) | Valencia del texto, de -1 (negativa) a 1 (positiva). Nulo si no se calculó. |
| ModelVersion | nvarchar(50) (nullable) | Versión del modelo o léxico que produjo la lectura, para poder reproducirla. |
| ScoresJson | nvarchar(1000) (nullable) | Puntajes por emoción en formato JSON. |
| AnalysisLatencyMs | int (nullable) | Tiempo que tardó el análisis, en milisegundos (evidencia de RF-AES-01). |

**Nota:** `ConversationTurnId` es opcional a propósito — una lectura de emoción puede
capturarse de forma continua (por ejemplo, por cámara) sin estar atada a un mensaje puntual.

---

## AvatarExpressions
Expresión visual que el avatar debería mostrar, derivada de una lectura de emoción.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la expresión. |
| SessionId | uniqueidentifier (FK → ExperimentSessions) | Sesión a la que pertenece. |
| EmotionReadingId | uniqueidentifier (FK → EmotionReadings) | Lectura de emoción que originó esta expresión. |
| ExpressionName | nvarchar(50) | Nombre de la expresión/animación a reproducir. |
| Intensity | float | Intensidad de la expresión (0 a 1). |
| ParametersJson | nvarchar(2000) | Parámetros adicionales para el motor de animación, en JSON. |
| IsFallback | bit | Indica si es una expresión por defecto (cuando no hay datos suficientes). |

---

## SurveyDefinitions / SurveyQuestions / SurveyResponses / SurveyAnswers
Conjunto de tablas para encuestas aplicadas antes/después del experimento.

**SurveyDefinitions** — plantilla de una encuesta (ej. "Cuestionario post-sesión v1").

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la definición. |
| Code | nvarchar(50) (UK) | Código único de la encuesta. |
| Version | nvarchar(20) | Versión de la plantilla. |
| Name | nvarchar(200) | Nombre visible. |
| IsActive | bit | Si la encuesta está habilitada para usarse. |

**SurveyQuestions** — cada pregunta dentro de una definición.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la pregunta. |
| SurveyDefinitionId | uniqueidentifier (FK → SurveyDefinitions) | Encuesta a la que pertenece. |
| Code | nvarchar(50) | Código corto de la pregunta. |
| Text | nvarchar(1000) | Enunciado de la pregunta. |
| Type | nvarchar(30) | Tipo de respuesta esperada (texto, escala, opción múltiple, etc.). |
| Required | bit | Si es obligatoria. |
| Order | int | Orden de aparición dentro de la encuesta. |

**SurveyResponses** — una respuesta completa de un usuario a una encuesta, en una sesión.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la respuesta. |
| SessionId | uniqueidentifier (FK → ExperimentSessions) | Sesión durante la cual se respondió. |
| SurveyDefinitionId | uniqueidentifier (FK → SurveyDefinitions) | Encuesta respondida. |
| OwnerUserId | uniqueidentifier (FK → UserAccounts) | Usuario que respondió. |
| StartedAtUtc | datetime2 | Momento en que empezó a responder. |
| SubmittedAtUtc | datetime2 (nullable) | Momento en que envió la respuesta completa. |

**Restricción:** no se puede repetir la combinación `SessionId` + `SurveyDefinitionId`
(un usuario no responde la misma encuesta dos veces en la misma sesión).

**SurveyAnswers** — la respuesta puntual a cada pregunta dentro de una `SurveyResponse`.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador de la respuesta puntual. |
| SurveyResponseId | uniqueidentifier (FK → SurveyResponses) | Respuesta general a la que pertenece. |
| SurveyQuestionId | uniqueidentifier (FK → SurveyQuestions) | Pregunta respondida. |
| Value | nvarchar(4000) | Valor de la respuesta (texto libre, número o código de opción, según `Type`). |

**Restricción:** no se puede repetir la combinación `SurveyResponseId` + `SurveyQuestionId`
(no se responde la misma pregunta dos veces dentro de la misma respuesta).

---

## ExperimentEvents
Bitácora genérica de eventos del experimento (errores, operaciones degradadas,
hitos), pensada para trazabilidad y depuración sin acoplarse a una tabla específica.

| Campo | Tipo | Descripción |
|---|---|---|
| Id | uniqueidentifier (PK) | Identificador del evento. |
| SessionId | uniqueidentifier (FK → ExperimentSessions) | Sesión relacionada. |
| Type | nvarchar(50) | Tipo de evento (ej. `DegradedOperation`, `SessionExpired`). |
| PayloadJson | nvarchar(4000) | Datos del evento en formato JSON libre. |
| OccurredAtUtc | datetime2 | Momento en que ocurrió el evento. |
| ExpiresAtUtc | datetime2 | Momento a partir del cual el evento puede purgarse/archivarse. |

**Uso previsto (contrato AI):** cuando el adaptador de generación de texto
(`ITextGenerationPort`) opera en modo degradado, debería registrarse aquí un evento
de tipo `DegradedOperation` (pendiente de conectar — actualmente el adaptador
reporta `degraded=true` en su resultado, pero la persistencia del evento en esta
tabla queda como trabajo futuro, fuera del alcance de esta entrega).

---

## Índices relevantes (más allá de las PK/FK)

| Tabla | Índice | Propósito |
|---|---|---|
| ExperimentSessions | `SessionCode` (único) | Búsqueda rápida por código legible. |
| ExperimentSessions | `InterlocutorUserId` | Consultar las sesiones donde un usuario es interlocutor. |
| ExperimentSessions | `Condition, InterlocutorUserId, Status` | Buscar sesiones disponibles para emparejar con un interlocutor. |
| UserAccounts | `NormalizedEmail` (único) | Evitar correos duplicados y acelerar login. |
| ConversationTurns | `ExperimentSessionId, SequenceNumber` (único) | Evitar turnos duplicados y ordenar la conversación eficientemente. |
| SurveyResponses | `SessionId, SurveyDefinitionId` (único) | Evitar respuestas duplicadas a la misma encuesta. |
| SurveyAnswers | `SurveyResponseId, SurveyQuestionId` (único) | Evitar respuestas duplicadas a la misma pregunta. |
| EmotionReadings / AvatarExpressions / ExperimentEvents / SessionAuditEntries | `SessionId` | Acelerar consultas de "todo lo relacionado con esta sesión". |

## Reglas de borrado

- Eliminar una `ExperimentSession` elimina en cascada sus datos dependientes
  (turnos, lecturas de emoción, expresiones, eventos, auditoría, asignación de condición).
- `UserAccounts` y `SurveyDefinitions` **no** se eliminan en cascada: si se borra un
  usuario o una definición de encuesta, las sesiones/respuestas que los referencian
  no desaparecen automáticamente (evita pérdida accidental de datos históricos).
- El interlocutor (`InterlocutorUserId`) usa `Restrict`: no se puede borrar un usuario mientras sea interlocutor de alguna sesión.
