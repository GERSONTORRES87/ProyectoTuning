# Spec Delta

## Purpose

Define cómo SUE obtiene la emoción del texto que escribe el participante, con qué vocabulario la guarda y qué hace cuando el clasificador externo no responde.

## ADDED Requirements

### Requirement: Clasificación con la API de emociones
El sistema SHALL clasificar cada turno del participante enviando su texto a la API de emociones configurada y SHALL guardar la emoción de mayor probabilidad, su confianza, la distribución completa y la versión del modelo que informa la API.

#### Scenario: La API responde
- **WHEN** el participante escribe «hoy me siento feliz» y la API devuelve `HAPPINESS` con confianza 0.91
- **THEN** la lectura del turno tiene emoción `joy`, confianza 0.91 y la versión del modelo de texto de la API

### Requirement: Vocabulario de emociones
El sistema SHALL usar el vocabulario `joy`, `sadness`, `anger`, `surprise`, `fear`, `disgust`, `confusion` y `neutral`, traduciendo las clases de la API así: `HAPPINESS`→`joy`, `SADNESS`→`sadness`, `ANGER`→`anger`, `SURPRISE`→`surprise`, `FEAR`→`fear`, `DISGUST`→`disgust`, `NEUTRAL`→`neutral`.

#### Scenario: Miedo y asco se conservan
- **WHEN** la API devuelve `FEAR`
- **THEN** la lectura se guarda con emoción `fear`

#### Scenario: Clase desconocida
- **WHEN** la API devuelve una clase fuera de las siete conocidas
- **THEN** se usa el respaldo local para ese turno

### Requirement: Respaldo local
Cuando la API de emociones no está configurada, no responde dentro de su tiempo límite o devuelve un error, el sistema SHALL clasificar el texto con el léxico local y SHALL NOT impedir ni retrasar más allá de ese tiempo límite el registro del turno.

#### Scenario: API caída
- **WHEN** la API de emociones devuelve `503` o no responde en 1500 ms
- **THEN** el turno se registra con la emoción del léxico local y la versión del léxico

#### Scenario: Sin configuración
- **WHEN** no hay URL de la API de emociones configurada
- **THEN** no se hace ninguna llamada de red y se usa el léxico local

### Requirement: Credencial de la API de emociones
El sistema SHALL autenticarse ante la API de emociones con un token Bearer tomado de la configuración del despliegue y SHALL NOT exponer ese token al cliente ni registrarlo.

#### Scenario: Token rechazado
- **WHEN** la API responde `401`
- **THEN** se usa el respaldo local y el registro del error no contiene el token
