# Spec Delta

## Purpose

Filtra los mensajes del participante en la condición `AI` antes de que lleguen al modelo de lenguaje: corta con una respuesta aprobada los que no deben llegar y orienta la respuesta de los demás con contexto recuperado de una base de conocimiento corta.

## ADDED Requirements

### Requirement: Base de conocimiento del filtro
El sistema SHALL cargar una base de conocimiento versionada con reglas de bloqueo (ejemplos, palabras clave y respuesta aprobada), pautas por tema (ejemplos y pauta) y pautas por emoción (emoción y pauta). Una base mal formada SHALL impedir el arranque.

#### Scenario: Base válida
- **WHEN** el API arranca con la base incluida
- **THEN** el filtro queda disponible con todas sus reglas y pautas

#### Scenario: Regla sin respuesta
- **WHEN** una regla de bloqueo no tiene respuesta aprobada
- **THEN** el API no arranca y el error nombra la regla

### Requirement: Bloqueo con respuesta aprobada
En la condición `AI`, cuando el mensaje del participante contiene una palabra clave de una regla de bloqueo o se parece a uno de sus ejemplos por encima del umbral configurado, el sistema SHALL responder con la respuesta aprobada de esa regla sin llamar al modelo de lenguaje.

#### Scenario: Señal de crisis
- **WHEN** el participante escribe «ya no quiero seguir viviendo»
- **THEN** el interlocutor responde la respuesta aprobada de la regla de crisis
- **AND** no se llama a ningún proveedor de generación

#### Scenario: Pregunta por la identidad del interlocutor
- **WHEN** el participante pregunta «¿eres una inteligencia artificial?»
- **THEN** responde la respuesta aprobada de esa regla, que no revela la condición

### Requirement: Contexto recuperado para el modelo
Cuando ninguna regla bloquea, el sistema SHALL enviar al modelo de lenguaje, junto al mensaje, hasta dos pautas por tema que superen el umbral de similitud y la pauta de la emoción detectada si existe.

#### Scenario: Mensaje sobre duelo
- **WHEN** el participante escribe «se murió mi abuela la semana pasada» y su emoción es `sadness`
- **THEN** la solicitud al modelo incluye la pauta de duelo y la pauta de `sadness`

#### Scenario: Mensaje sin coincidencias
- **WHEN** ningún tema supera el umbral y la emoción es `neutral`
- **THEN** la solicitud incluye solo la pauta de `neutral`

### Requirement: Filtro sin embeddings
Si el servicio de embeddings no está configurado, falla o supera su tiempo límite, el sistema SHALL comprobar las reglas de bloqueo por sus palabras clave, sin pautas por tema, y SHALL continuar la conversación.

#### Scenario: Embeddings caídos con señal de crisis
- **WHEN** el servicio de embeddings no responde y el mensaje contiene la palabra clave «suicidarme»
- **THEN** se responde la respuesta aprobada de la regla de crisis

#### Scenario: Embeddings caídos sin coincidencia
- **WHEN** el servicio de embeddings no responde y ninguna palabra clave coincide
- **THEN** el mensaje llega al modelo con la pauta de la emoción detectada

### Requirement: Trazabilidad del filtro
El sistema SHALL registrar en el evento de la respuesta del interlocutor qué hizo el filtro (`blocked` con la regla, `context` con las pautas, o `keywords` cuando no hubo embeddings) y SHALL ocultar ese dato al participante igual que el proveedor y el modelo.

#### Scenario: Investigador revisa una respuesta bloqueada
- **WHEN** un `Researcher` consulta los eventos de una sesión con una respuesta bloqueada
- **THEN** ve el proveedor `guard` y la regla aplicada

#### Scenario: Participante revisa sus eventos
- **WHEN** el participante consulta los eventos de su sesión
- **THEN** no ve el resultado del filtro, el proveedor ni el modelo

### Requirement: Solo condición AI
El sistema SHALL aplicar el filtro únicamente a los mensajes del participante en la condición `AI`; en la condición `Human` el mensaje llega a la persona sin cambios.

#### Scenario: Condición Human
- **WHEN** el participante de una sesión `Human` escribe una frase que coincide con una regla de bloqueo
- **THEN** no se genera respuesta automática y el mensaje queda para el interlocutor humano
