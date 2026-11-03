## MODIFIED Requirements

### Requirement: Aislamiento por propietario en la conversación

El sistema SHALL identificar al emisor por el token: el propietario escribe como Participant y el interlocutor asignado como Interlocutor. SHALL permitir consulta a Researcher y Administrator y SHALL responder `404 SESSION_NOT_FOUND` a terceros sin privilegios. Un observador no puede suplantar un emisor.

#### Scenario: Conversación de sesión ajena
- **WHEN** un tercero sin asignación ni privilegios consulta o escribe
- **THEN** recibe `404 SESSION_NOT_FOUND` y no se persiste ningún turno

#### Scenario: Token sin identificador de usuario
- **WHEN** el token no permite resolver el usuario
- **THEN** recibe `401`

#### Scenario: Interlocutor humano
- **WHEN** la persona asignada envía un mensaje
- **THEN** se persiste como Interlocutor en esa sesión

#### Scenario: Observador
- **WHEN** un Researcher consulta una conversación ajena
- **THEN** obtiene los turnos sin permiso para enviar en nombre del participante

## ADDED Requirements

### Requirement: Mensajes coordinados por sesión

El sistema SHALL conservar secuencias únicas y consecutivas cuando se envían mensajes concurrentes en una sesión. Una respuesta generada SHALL guardarse sólo si la sesión continúa activa al finalizar la generación.

#### Scenario: Dos mensajes durante generación
- **WHEN** llegan dos mensajes antes de finalizar la primera respuesta
- **THEN** se conservan ambos mensajes y sus respuestas válidas sin colisión de secuencia

#### Scenario: Cierre durante generación
- **WHEN** la sesión termina mientras se genera la respuesta
- **THEN** se conserva el mensaje ya aceptado y no se agrega una réplica posterior al cierre

### Requirement: Metadatos compatibles con ensayo ciego

El sistema SHALL omitir condición, proveedor y modelo de la respuesta entregada al Participant. La interfaz SHALL mostrar al interlocutor con una etiqueta neutral.

#### Scenario: Respuesta del interlocutor
- **WHEN** el Participant recibe una respuesta
- **THEN** el contrato y la interfaz no identifican si procede de Human o AI
