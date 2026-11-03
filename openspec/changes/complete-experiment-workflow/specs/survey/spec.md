## MODIFIED Requirements

### Requirement: Envío único por sesión y definición

El sistema SHALL aceptar un único envío definitivo por sesión y definición después del cierre. El primer POST válido SHALL persistir las respuestas y `StartedAtUtc`/`SubmittedAtUtc` atómicamente. SHALL validar preguntas, valores y respuestas obligatorias antes de guardar.

#### Scenario: Inicio de la respuesta
- **WHEN** se envía por primera vez una encuesta válida de sesión terminal
- **THEN** se persisten sus respuestas y ambas fechas y se devuelve el identificador

#### Scenario: Envío definitivo
- **WHEN** el primer POST se completa correctamente
- **THEN** las respuestas aparecen en los resultados sin un segundo POST

#### Scenario: Segundo envío
- **WHEN** se reenvía una encuesta ya presentada
- **THEN** se devuelve `409 ALREADY_SUBMITTED` sin modificar respuestas

#### Scenario: Definición inexistente
- **WHEN** la definición indicada no existe
- **THEN** se devuelve `400 SURVEY_NOT_FOUND`

#### Scenario: Sesión todavía activa
- **WHEN** se intenta enviar sobre Created o Active
- **THEN** se devuelve `400 SURVEY_NOT_AVAILABLE` sin crear respuesta

#### Scenario: Respuesta inválida
- **WHEN** faltan respuestas obligatorias o se incluyen preguntas o valores inválidos
- **THEN** se devuelve `400` sin guardar datos parciales

## ADDED Requirements

### Requirement: Preguntas iniciales utilizables

El sistema SHALL inicializar una definición activa con preguntas ordenadas y validadas, incluyendo percepción del interlocutor y evaluación de la interacción. La inicialización repetida SHALL conservar la definición y no duplicar preguntas.

#### Scenario: Base nueva
- **WHEN** se inicia una base sin encuesta
- **THEN** una sesión terminada puede consultar y responder preguntas reales
