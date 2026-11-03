# Proposal

## Why

La revisión del MVP encontró un alta de sesión incompatible entre Web y API, conversación humana sin interfaz compartida, permisos de rol incompletos y encuestas cuyas respuestas se descartan. Se necesita un recorrido verificable desde el registro hasta los resultados, con datos persistidos.

## What Changes

- Conectar la Web con sesiones, conversación humana e IA, cierre, encuesta, emociones, avatar y resultados reales.
- Mantener el interlocutor humano ligado a su identidad y coordinar escrituras de conversación por sesión.
- **BREAKING**: el registro público concede Participant; la administración concede roles desde una operación autenticada.
- Entregar capacidades contextuales de participación sin revelar la condición ni el proveedor al participante.
- **BREAKING**: un POST válido de encuesta guarda las respuestas y finaliza el envío; los posteriores producen conflicto.
- Completar auditoría de transiciones y eventos, orden estable y expiración del flujo consultable.
- Sembrar preguntas reutilizables y cuentas de demostración únicamente en Development, sin introducir sesiones sintéticas automáticamente.
- Añadir pruebas funcionales y documentar el arranque reproducible con SQL Server.

## Capabilities

### New Capabilities

- `web-experiment`: recorrido interactivo persistido del experimento para participante, interlocutor e investigadores.

### Modified Capabilities

- `authentication`: registro Participant y concesión de roles por administrador.
- `session-lifecycle`: proyección contextual de participación y cegamiento del participante.
- `conversation-turns`: conversación compartida, coordinación de mensajes y contratos sin condición revelada.
- `survey`: preguntas iniciales y envío definitivo validado en una sola operación.
- `experiment-results`: datos reales y condición oculta en la vista del participante.

## Impact

Se modifican contratos y servicios existentes en Application, Infrastructure, API y Web, sus pruebas y la documentación de ejecución. Se conserva SQL Server y las migraciones existentes; no se actualizan dependencias NuGet ni se agregan métricas o cancelación en lote.
