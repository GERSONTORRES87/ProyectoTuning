# Consulta administrativa de encuestas

La vista `/surveys` permite a `Administrator` y `Researcher` consultar las
encuestas enviadas por agente. Filtra por agente y condición experimental,
pagina el listado y muestra las respuestas de la encuesta seleccionada.

La referencia es la encuesta `default-v1`, versión `1.0`, de Azure:

- `perceived_interlocutor`: interlocutor percibido, tipo `choice`.
- `empathy`: empatía, tipo `scale`, valores enteros de 1 a 5.
- `understanding`: comprensión, tipo `scale`, valores enteros de 1 a 5.
- `comments`: comentario opcional, tipo `text`.

El gráfico calcula los promedios de las respuestas válidas a las escalas de
empatía y comprensión dentro de la selección. El detalle conserva las preguntas
y la versión referenciadas por cada respuesta.

`GET /api/surveys/results?page=1&pageSize=50&participantId=<opcional>` devuelve
únicamente respuestas con `SubmittedAtUtc`, junto con su participante, sesión,
definición y respuestas individuales. Exige rol `Administrator` o `Researcher`.
La consulta es de solo lectura y no crea definiciones ni respuestas.

La pantalla usa la API configurada en `Api:BaseUrl`; cada entorno conserva su
propia base. Sin encuestas enviadas muestra el estado vacío y no calcula valores
de ejemplo. El formulario de respuesta existente es independiente de esta vista.
