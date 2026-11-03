## Purpose

Permitir ejecutar desde Blazor el experimento completo con información persistida, capacidades de participación explícitas y actualizaciones de la conversación compartida.

## ADDED Requirements

### Requirement: Inicio de sesión experimental

La Web SHALL registrar participantes, autenticarlos y crear sesiones sin enviar una condición inválida. SHALL permitir recuperar sesiones y seleccionar una sesión concreta.

#### Scenario: Participante nuevo
- **WHEN** un usuario se registra e inicia su sesión experimental
- **THEN** obtiene una sesión persistida y puede abrir su conversación

### Requirement: Conversación humana compartida

La Web SHALL permitir unirse por código como interlocutor y consultar periódicamente turnos nuevos y estado. SHALL cancelar esa consulta al abandonar la página y evitar envíos solapados desde la misma vista.

#### Scenario: Dos usuarios conversan
- **WHEN** un propietario y su interlocutor abren la misma sesión y envían mensajes
- **THEN** ambos reciben los mensajes sin recargar la página

### Requirement: Cierre y temporización visibles

La Web SHALL mostrar estado y tiempo restante, permitir al propietario cerrar su sesión activa y deshabilitar envío al llegar a estado terminal. SHALL ofrecer la encuesta tras el cierre.

#### Scenario: Sesión completada
- **WHEN** el propietario termina la sesión
- **THEN** la conversación deja de admitir mensajes y se ofrece la encuesta

### Requirement: Datos experimentales reales

La Web SHALL consultar y presentar emociones, expresión de avatar, preguntas y resultados desde la API. SHALL mostrar ausencia de datos de forma explícita y SHALL NOT presentar métricas inventadas como resultados.

#### Scenario: Consulta tras encuesta
- **WHEN** el participante envía su encuesta y abre resultados
- **THEN** observa las respuestas realmente persistidas de su sesión

### Requirement: Vistas según rol y participación

La Web SHALL reservar las operaciones administrativas al Administrator y la consulta transversal a Researcher o Administrator. SHALL distinguir propietario, interlocutor y observador para habilitar las acciones de sesión.

#### Scenario: Investigador observa
- **WHEN** un Researcher abre una sesión ajena
- **THEN** puede consultar sus datos sin enviar mensajes ni completar la sesión como propietario
