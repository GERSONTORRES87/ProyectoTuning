# Design

## Context

El cambio conserva los cinco proyectos y SQL Server. Ver proposal.md para la motivación. La Web dispone de clientes HTTP y autenticación en circuito; faltan operaciones y vistas conectadas. Hay entidades de encuesta y tablas de respuestas ya migradas.

## Goals / Non-Goals

**Goals:** completar y verificar el recorrido persistido con ambos tipos de interlocutor; mantener permisos definidos por identidad; conservar trazabilidad y contratos coherentes.

**Non-Goals:** actualizar NuGet, crear métricas agregadas, cancelación en lote, sustituir SQL Server o integrar captura audiovisual real. La generación puede usar el fallback por reglas ya previsto si no hay claves externas.

## Decisions

- Ampliar los clientes y componentes existentes. Usar polling incremental cancelable; SignalR no es necesario para esta entrega.
- Añadir participación contextual Owner/Interlocutor/Observer. La API decide el emisor y los permisos; las etiquetas de condición se ocultan a Participant.
- Coordinar escrituras por sesión en una instancia del backend y recargar el agregado después de esperar la generación. No mantener una transacción de base abierta durante una llamada externa.
- Guardar encuesta y respuestas con un POST definitivo y validación previa. Es un cambio de contrato explícito respecto a los dos POST de la spec anterior.
- Inicializar preguntas reutilizables siempre. Crear cuentas de demostración con hash válido sólo en Development; no cargar conversaciones sintéticas automáticamente.
- Conservar eventos históricos para resultados, aplicando la expiración al feed de polling. Ordenar por fecha e identificador con el mismo criterio usado por el cursor.
- Ejecutar pruebas de dominio, aplicación, contratos y SQL Server; recorrer la interfaz cuando el motor esté disponible. Registrar cualquier impedimento real del entorno.

## Risks / Trade-offs

- Coordinación en memoria: cubre la instancia de desarrollo universitaria; un despliegue con varias réplicas necesita coordinación distribuida o transacciones/colas apropiadas.
- Credenciales de demostración conocidas: se limitan a Development y se documentan como cuentas locales.
- Falta de LocalDB: se instala el requisito de SQL Server antes de acreditar las pruebas de persistencia; no se sustituye por SQLite.
- IA por reglas: permite probar funcionalmente el flujo, pero no acredita calidad de un modelo externo ni de análisis audiovisual.

## Migration Plan

Aplicar las migraciones existentes, inicializar preguntas y cuentas de desarrollo, ejecutar pruebas y recorrer ambos caminos. No se eliminan datos existentes. Para revertir código se conserva la base; las respuestas de encuesta válidas permanecen persistidas.
