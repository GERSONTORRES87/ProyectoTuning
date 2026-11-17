# Spec Delta

## Purpose

Define qué proveedor y qué modelo producen las respuestas del interlocutor en la condición `AI`, cómo se degrada la cadena hasta el adaptador por reglas y cómo el `Administrator` cambia esa selección sin reiniciar el sistema.

## ADDED Requirements

### Requirement: Proveedores de generación disponibles
El sistema SHALL ofrecer cuatro proveedores identificados como `local`, `openrouter`, `openai` y `rule-based`. Un proveedor externo está disponible cuando tiene URL base y modelo configurados y, salvo `local`, una clave real; `local` no exige clave. `rule-based` está siempre disponible.

#### Scenario: Proveedor local sin clave
- **WHEN** `local` tiene URL base y modelo configurados y no tiene clave
- **THEN** `local` figura como disponible

#### Scenario: Proveedor externo con clave de marcador
- **WHEN** `openrouter` tiene como clave un marcador de posición
- **THEN** `openrouter` figura como no disponible

### Requirement: Selección vigente de proveedor y modelo
El sistema SHALL generar cada respuesta de la condición `AI` con el proveedor y el modelo seleccionados en el momento del turno y, si ese proveedor falla o no está disponible, con `rule-based` marcado como degradado. Sin selección guardada SHALL usar el orden de proveedores de la configuración del despliegue.

#### Scenario: Responde el proveedor seleccionado
- **WHEN** la selección es `local` con el modelo `qwen2.5:1.5b` y el proveedor responde
- **THEN** el turno del interlocutor queda con proveedor `local`, modelo `qwen2.5:1.5b` y sin marca de degradado

#### Scenario: Falla el proveedor seleccionado
- **WHEN** el proveedor seleccionado agota su tiempo o devuelve error
- **THEN** responde `rule-based` y el turno queda marcado como degradado

#### Scenario: Sin selección guardada
- **WHEN** nunca se guardó una selección
- **THEN** los proveedores se intentan en el orden de la configuración del despliegue

#### Scenario: Selección solo reglas
- **WHEN** la selección es `rule-based`
- **THEN** responde el adaptador por reglas sin llamar a ningún proveedor externo

### Requirement: Consulta de la configuración de IA
El sistema SHALL exponer `GET /api/admin/ai-settings`, solo para `Administrator`, con el proveedor y modelo seleccionados, quién y cuándo los cambió, y por cada proveedor su identificador, si está disponible y su modelo por defecto. La respuesta MUST NOT incluir claves de API ni partes de ellas.

#### Scenario: Administrator consulta
- **WHEN** un `Administrator` llama a `GET /api/admin/ai-settings`
- **THEN** recibe `200` con la selección vigente y los cuatro proveedores con su disponibilidad

#### Scenario: Otro rol consulta
- **WHEN** un `Researcher` o un `Participant` llama a `GET /api/admin/ai-settings`
- **THEN** recibe `403`

#### Scenario: Sin claves en la respuesta
- **WHEN** `openrouter` tiene una clave real configurada
- **THEN** la respuesta indica que está disponible pero no contiene la clave

### Requirement: Cambio de la configuración de IA
El sistema SHALL exponer `PUT /api/admin/ai-settings`, solo para `Administrator`, que recibe proveedor y modelo, los guarda con el usuario y la hora UTC del cambio y aplica la selección desde el siguiente turno, sin reiniciar el sistema. El cuerpo MUST NOT aceptar claves de API.

#### Scenario: Cambio aplicado
- **WHEN** un `Administrator` guarda `openrouter` con el modelo `nvidia/nemotron-3.5-lightning:free` y `openrouter` está disponible
- **THEN** recibe `200` con la selección guardada
- **AND** el siguiente turno de una sesión `AI` se pide a `openrouter` con ese modelo

#### Scenario: Proveedor desconocido o modelo inválido
- **WHEN** el proveedor no es uno de los cuatro, o el modelo está vacío, supera 200 caracteres o contiene caracteres fuera de letras, dígitos y `. _ : / -`
- **THEN** recibe `400` con código `AI_SETTINGS_INVALID` y la selección no cambia

#### Scenario: Proveedor no disponible
- **WHEN** el proveedor pedido no está disponible, por ejemplo sin clave
- **THEN** recibe `422` con código `AI_PROVIDER_UNAVAILABLE` y la selección no cambia

#### Scenario: Otro rol intenta cambiar
- **WHEN** un `Researcher` o un `Participant` llama a `PUT /api/admin/ai-settings`
- **THEN** recibe `403` y la selección no cambia

### Requirement: Rehabilitación del proveedor al guardarlo
El sistema SHALL volver a intentar el proveedor seleccionado después de guardar la configuración, aunque antes hubiera quedado fuera de la cadena por credenciales, cuota o fallos repetidos.

#### Scenario: Proveedor sacado por cuota
- **WHEN** `openrouter` quedó fuera de la cadena por cuota agotada y un `Administrator` vuelve a guardar `openrouter`
- **THEN** el siguiente turno se pide de nuevo a `openrouter`

### Requirement: Configuración de IA en Administración
La Web SHALL mostrar en `/admin`, solo al `Administrator`, una sección «Inteligencia artificial» con el proveedor y modelo vigentes, la disponibilidad de cada proveedor y un formulario para cambiarlos, mostrando el resultado o el error del API.

#### Scenario: Administrator cambia el proveedor
- **WHEN** un `Administrator` elige `local`, escribe `qwen2.5:1.5b` y guarda
- **THEN** la sección muestra la selección nueva con su hora de cambio

#### Scenario: Researcher en Administración
- **WHEN** un `Researcher` abre `/admin`
- **THEN** no ve la sección «Inteligencia artificial»
