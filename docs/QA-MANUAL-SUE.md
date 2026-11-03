# QA manual de caja negra — SUE

| | |
|---|---|
| **Web** | https://sue-front-end.thankfulocean-61ca6024.eastus.azurecontainerapps.io |
| **API** | `sue-back-end.internal.thankfulocean-61ca6024.eastus.azurecontainerapps.io`: ingreso **interno** de Azure, QA no la alcanza (ver 5) |
| **Versión** | `0.2.0-20261023-172558`, Docker en Azure Container Apps, *Production* (confirmar que sigue vigente) |
| **Modalidad** | Individual: todos los QA ejecutan lo mismo. **≈ 5 h por ronda**, 2 rondas |
| **Casos** | 33 casos de QA (27 P1, 6 P2) + 10 verificaciones API para desarrollo |

**Reglas.** Solo se valida lo que el usuario ve y hace: nada de código, base de datos, logs ni contenedores, y no se despliega nada. El resultado esperado sale de los requisitos (`README.md`, `docs/demo.md`, `openspec/specs` y `openspec/changes/complete-experiment-workflow`). Si un requisito es ambiguo, el caso dice **(verificar)**: se anota lo observado y se consulta. Los textos entre «» son ejemplos del mensaje actual; si cambia el texto pero no el sentido, el caso pasa.

---

## 1. Preparación (30 min)

1. **Ventanas.** Abrir 3 sesiones de navegador a la vez, una por rol: Chrome normal (P1), Chrome incógnito (P2) y Edge o Firefox (P3 o INV). Dos pestañas de la misma ventana comparten usuario.
2. **Cuentas.** Cada QA registra las suyas: `qa.p1.<iniciales>@example.com`, `qa.p2…`, `qa.p3…`, `qa.inv…` y `qa.admin…`, con una contraseña de prueba propia (8+ caracteres). Sin correos reales. Única excepción: para AUT-05 se registra además `qa.reset…` con un buzón de pruebas que el QA pueda abrir (una cuenta de correo creada para pruebas o un alias), porque el caso necesita recibir el correo.
3. **Roles.** El registro público solo crea Participant. Un Administrator **ya existente** del equipo promueve `qa.inv…` a Researcher y `qa.admin…` a Administrator desde Configuración. Si no hay ningún Administrator en Azure, los casos marcados **[INV]** o **[ADMIN]** quedan ⛔.
4. **Tiempos.** Confirmar con el equipo la duración máxima (requisito: **300 s**) y la inactividad (requisito: **120 s**).
5. **Calentar.** Abrir la Web un minuto antes: tras un rato sin uso, la primera carga puede tardar más de 30 s.
6. **Datos.** El entorno es compartido y no se borra nada: solo se tocan cuentas y sesiones `qa.*` propias.
7. **Condición.** P1 nunca ve si su sesión es Human o AI. Para saberlo, INV mira **Resultados → Sesiones para investigación** (`código · condición · estado`). Para conseguir una sesión Human o AI concreta, crear sesiones hasta que salga y confirmarla con INV.

**Sesiones ancla** (crearlas una vez y reutilizarlas): **S-AI** (AI, 5 mensajes emocionales, finalizada), **S-HUM** (Human con P1 dueño y P2 interlocutor), **S-TO** (expirada) y **S-CAN** (cancelada).

**Técnicas:** PE partición de equivalencia · VL valores límite · TE transición de estados · TD tabla de decisión · CU caso de uso · AE adivinanza de errores.

---

## 2. Casos

Marcar **Res.** con ✅ pasó · ❌ falló · ⛔ bloqueado · ➖ no aplica.

### Acceso y roles

| ID | P | Téc. | Pasos | Esperado | Res. |
|---|---|---|---|---|---|
| AUT-01 | P1 | PE | Registrar P1 con datos válidos. Repetir el registro con el mismo correo y luego con el correo en MAYÚSCULAS. | Entra al panel con nombre, rol **Participant** e identificador. Los dos registros repetidos se rechazan («ya existe un usuario…»); el de mayúsculas, (verificar). | ☐ |
| AUT-02 | P2 | VL | En el registro: enviar vacío; correos `abc`, `abc@`, `@x.com`; contraseña de 7 y de 8 caracteres; confirmación distinta. | Mensaje de validación en cada caso; 7 se rechaza y 8 se acepta; no se crea ninguna cuenta inválida. | ☐ |
| AUT-03 | P1 | PE | Iniciar sesión con P1 correcto; luego con contraseña errónea; luego con un correo inexistente. | El correcto entra. Los dos fallidos muestran **el mismo** mensaje, que no revela qué dato falló. | ☐ |
| AUT-04 | P1 | TD | Sin sesión, abrir `/workspace`, `/sessions`, `/conversations`, `/results`, `/settings`. Iniciar sesión desde ahí. Abrir `/login?returnUrl=https://example.com` e iniciar sesión. Cerrar sesión y pulsar Atrás. | Todas las rutas llevan al login sin mostrar datos; tras entrar vuelve a la página pedida. El `returnUrl` externo termina en el panel, nunca fuera de SUE. Tras cerrar sesión, Atrás no muestra contenido. | ☐ |
| AUT-05 | P1 | TE | Con la cuenta `qa.reset…` con sesión abierta en otra ventana: en login pulsar «¿Olvidaste tu contraseña?», escribir su correo y «Enviar enlace»; repetir con un correo inexistente. Abrir el correo recibido (revisar también no deseado) y seguir el enlace. Poner una contraseña nueva dos veces y «Cambiar contraseña». Entrar con la nueva y luego intentar con la anterior. Volver a abrir el mismo enlace y enviarlo. | La confirmación es **idéntica** para los dos correos. Llega un solo correo, con un enlace que vence en 30 minutos. El cambio se confirma y ofrece ir al login. La contraseña nueva entra y la anterior no. La ventana con la sesión abierta antes del cambio pide iniciar sesión de nuevo al siguiente clic. El enlace reusado se rechaza y ofrece pedir uno nuevo. Si el correo no llega en 5 minutos: ⛔ (falta configurar la cuenta Gmail del remitente). | ☐ |
| ROL-01 | P1 | TD | Recorrer Panel, Sesiones, Resultados, Administración, Configuración y una conversación activa con P1, con INV **[INV]** y con ADMIN **[ADMIN]**. | P1 no ve: tarjeta de investigación, cola de interlocutores, listado de investigación, Administración, «Asignar rol» ni cancelación. INV ve cola, listado y las estadísticas de Administración, pero no «Roles de usuario», «Asignar rol» ni cancelación. ADMIN ve todo. | ☐ |
| ROL-02 | P1 | CU | **[ADMIN]** En Administración → «Roles de usuario», buscar a P3 por su correo, elegir Researcher y pulsar «Guardar» (también se puede en Configuración con su identificador). P3 recarga, luego cierra sesión y vuelve a entrar. Probar el identificador `abc` y uno inexistente. ADMIN intenta asignarse Participant a sí mismo. Al final, devolver P3 a Participant. | Confirmación del cambio; P3 ve los permisos de investigador solo tras reingresar. `abc` e inexistente dan error sin cambios. Quitarse el propio rol de admin se rechaza. | ☐ |

### Sesiones y tiempos

| ID | P | Téc. | Pasos | Esperado | Res. |
|---|---|---|---|---|---|
| SES-01 | P1 | CU | Sesiones → «Crear sesión experimental» con doble clic rápido. Volver a Sesiones. | Se crea **una** sesión en estado **Created**, con código `EXP-` + 8 caracteres hexadecimales en mayúscula. Aparece en «Mis sesiones»; «Encuesta» y «Resultados» no se ofrecen todavía. | ☐ |
| SES-02 | P1 | TE | Created → «Iniciar sesión experimental» → «Finalizar y responder encuesta». En cada estado mirar qué acciones ofrece la pantalla. | **Active**: «Tiempo máximo restante» cerca de 05:00, campo de mensaje y botón de finalizar. **Completed**: lleva a la encuesta, sin campo de mensaje y sin opciones de iniciar, finalizar ni cancelar. | ☐ |
| SES-03 | P1 | TD | Durante toda la ronda, buscar Human/AI en todas las pantallas de P1 y en su JSON descargado. Al final de la ronda, **[INV]** ver la condición de todas las sesiones creadas y de una antes y después de finalizarla. | P1 nunca ve la condición. Las condiciones están repartidas (no salen todas iguales) y la de cada sesión no cambia. | ☐ |
| SES-04 | P1 | TE | **[ADMIN]** Abrir una sesión Active de P1. Con el motivo vacío, mirar el botón; escribir un motivo y cancelar. Mirar la ventana de P1 sin recargar. | Sin motivo el botón está deshabilitado. Con motivo la sesión queda **Cancelled**; P1 la ve terminada en segundos y ya no puede escribir. En una sesión terminada no aparece la opción de cancelar. | ☐ |
| SES-05 | P2 | AE | Con 2 o más sesiones: cambiar el selector en Conversación, Emociones y Resultados. Abrir `/conversations` sin parámetro. Abrir `/conversations?sessionId=` con un GUID aleatorio. Con una cuenta sin sesiones, abrir Conversación. | Cada vista muestra los datos de la sesión elegida, sin mezclar. Sin parámetro carga la última sesión. El GUID aleatorio da «Sesion no encontrada» con «Reintentar», sin pantalla en blanco. La cuenta sin sesiones ve una invitación a crear una o unirse. | ☐ |
| TMP-01 | P1 | VL | Iniciar una sesión AI y enviar un mensaje cada ~60 s. Al pasar la duración máxima, intentar enviar otro. | A ≈ 300 s pasa a **TimedOut** sin recargar: desaparece el campo de mensaje y aparecen encuesta y resultados. El envío posterior se rechaza. Esta sesión queda como **S-TO**. | ☐ |
| TMP-02 | P1 | VL | Sesión A: iniciar y no tocarla. Sesión B: iniciar y enviar un mensaje a los ~100 s. Seguir con otros casos mientras corre el reloj. | A pasa a **TimedOut** a ≈ 120 s. B **no** expira a los 120 s; expira ≈ 120 s después de su último mensaje. | ☐ |

### Conversación

| ID | P | Téc. | Pasos | Esperado | Res. |
|---|---|---|---|---|---|
| EMP-01 | P1 | PE | En una sesión Human de P1, P2 prueba en «Código de invitación»: el código correcto; luego P1 su propio código; P2 `EXP-00000000`; P2 el código de una sesión AI; campo vacío; P3 el código ya ocupado por P2; P2 el código de una sesión terminada. | Correcto: P2 entra y el selector indica «Interlocutor». Propio código: «no puedes ser interlocutor de tu propia sesión». Inexistente: «no encontrada». AI, ocupado y terminado: rechazo por conflicto. Vacío: botón deshabilitado. | ☐ |
| EMP-02 | P1 | CU | **[INV]** Sesiones → «Actualizar cola», antes y después de EMP-01. Probar «Observar», «Tomar conversación» y «Asignar usuario» (con el identificador de P3) en sesiones de la cola. | La cola lista solo sesiones **Human sin interlocutor**, nunca AI; la de EMP-01 desaparece. «Observar» abre en modo observador, sin campo de mensaje. «Tomar» y «Asignar» dejan como interlocutor a INV y a P3 respectivamente. | ☐ |
| CHU-01 | P1 | CU | **S-HUM.** P2 abre la sesión antes de que P1 la inicie. P1 inicia y escribe `Hola`; P2 responde. Después, alternar rápido 5 mensajes de cada lado. Recargar las dos ventanas. | Antes de iniciar, P2 ve que la sesión no ha comenzado y no puede escribir. Los mensajes llegan a la otra ventana en segundos, sin recargar. Cada uno ve los suyos como «Tú»; P1 ve a P2 como «SUE» y P2 ve a P1 como «Participante». Los 10 mensajes aparecen sin duplicados y en **el mismo orden** en las dos ventanas, también tras recargar. | ☐ |
| CHU-02 | P1 | TD | **S-HUM.** En la ventana de P2: buscar iniciar, finalizar y cancelar; copiar de P1 las URL de Emociones, Avatar, Encuesta y Resultados y abrirlas. Buscar en cada lado el nombre o correo del otro. P1 finaliza. | P2 no tiene esos controles. Emociones, Avatar y Resultados: «no encontrada», sin datos. Encuesta: aviso de que la responde el dueño. Ninguno ve la identidad del otro ni la condición. Al finalizar P1, P2 ve la sesión terminada sin recargar y **sin** «Responder encuesta». | ☐ |
| CHU-03 | P1 | TD | **[INV]** «Observar» **S-HUM** mientras P1 y P2 conversan. | INV ve los mensajes llegar y la condición real, sin campo de mensaje. | ☐ |
| CIA-01 | P1 | CU | Sesión AI activa. Enviar `Hola, ¿cómo estás?` y luego 4 mensajes más (con el doble clic rápido en uno de ellos), cronometrando cada respuesta. Recargar con F5. | Cada mensaje recibe una respuesta etiquetada «SUE», sin mención a IA, proveedor ni modelo. Respuesta en ≲ 12 s; mientras tanto la entrada está deshabilitada. El doble clic envía **uno** solo. Tras recargar, el historial sigue completo y en orden. Anotar si las respuestas suenan a plantilla. | ☐ |
| CIA-02 | P1 | VL | Enviar: vacío o solo espacios; 4000 caracteres e intentar escribir uno más; `ñandú á é ¿? ¡!` con un emoji; `<script>alert(1)</script>`; `' OR 1=1 --`. Revisar también Resultados. | Vacío: «Enviar» deshabilitado. 4000 se acepta y el campo no deja pasar de ahí. Todo se muestra como **texto literal**: no se ejecuta nada ni se rompe la página. | ☐ |
| CIA-03 | P2 | AE | Con la conversación abierta, poner el navegador sin conexión (DevTools → Network → Offline) y enviar; volver a conectar. | Error entendible y sin bloqueo; al reconectar se puede seguir sin duplicados. Aparece un aviso de reconexión. | ☐ |

### Emociones y avatar

| ID | P | Téc. | Pasos | Esperado | Res. |
|---|---|---|---|---|---|
| EMO-01 | P1 | PE | **S-AI.** Enviar `Estoy muy feliz y contento`, `Me siento triste y solo`, `Estoy furioso, siento rabia`, `Qué sorpresa, estoy sorprendido` y `Ok`. Abrir Emociones, Panel y Resultados. | Una lectura por cada mensaje del participante (las respuestas no generan lecturas): alegría, tristeza, enojo, sorpresa y neutral (si alguna sale distinta, anotarla). Orden cronológico; el número de lecturas coincide en Emociones, Panel y Resultados. | ☐ |
| EMO-02 | P2 | VL | En Emociones guardar intensidad 0, 7 y 10. Revisar si aparece el formulario con la sesión Created, Completed, como P2 y como INV. Con la página abierta, dejar vencer la sesión y guardar. | 0 %, 70 % y 100 %, con mensaje de guardado. El formulario solo aparece al dueño con la sesión **Active**. Tras vencer, guardar da error y no se crea la lectura. | ☐ |
| EMO-03 | P2 | PE | En una sesión nueva sin lecturas abrir Avatar. Registrar alegría, tristeza, enojo, sorpresa y Neutral, pulsando «Actualizar expresión» tras cada una. | Sin lecturas: **Neutral de respaldo** («no representa una medición»), sin intensidad. Cada emoción muestra su propia expresión y símbolo con intensidad. Neutral registrado muestra intensidad (no es respaldo). Manda la última registrada. Resultados lista **todas** las expresiones. | ☐ |

### Encuesta y resultados

| ID | P | Téc. | Pasos | Esperado | Res. |
|---|---|---|---|---|---|
| ENC-01 | P1 | TE | Abrir Encuesta con una sesión Active, con **S-AI**, **S-TO** y **S-CAN**, y también como P2 y como INV **[INV]**. | Active: «disponible cuando la sesión termine». Completed, TimedOut y Cancelled: formulario disponible. P2 e INV: aviso de que la responde el dueño, sin formulario. | ☐ |
| ENC-02 | P1 | PE | **S-AI.** Revisar las preguntas. Enviar con las obligatorias vacías. Pegar más de 4000 caracteres en el comentario. | 4 preguntas en orden: con quién crees que conversaste («Una persona» / «Inteligencia artificial» / «No estoy seguro/a»), empatía 1–5, comprensión 1–5 y comentario opcional, cada una marcada como obligatoria u opcional. Con vacías no se envía nada. El comentario se corta en 4000. | ☐ |
| ENC-03 | P1 | TE | **S-AI.** Responder todo, con un comentario con tildes y emoji, y pulsar enviar con doble clic. Recargar Encuesta. Abrir Resultados. | Confirmación de guardado. Se guarda **una** sola respuesta. Al recargar se ve «ya enviada», sin formulario. Resultados muestra la fecha y cada respuesta junto a su pregunta, con el texto íntegro. | ☐ |
| RES-01 | P1 | CU | Como P1: abrir Resultados de una sesión Active; luego de **S-AI**; luego de una sesión terminada sin datos. Descargar el JSON de **S-AI**. | Active: «disponible cuando termine». **S-AI**: los conteos de mensajes, lecturas, expresiones y encuestas coinciden con lo hecho; conversación completa y en orden; **sin** condición ni operaciones degradadas. Sin datos: textos explícitos de vacío, sin cifras inventadas. El JSON es válido y no trae condición, identificadores de usuario ni tokens. | ☐ |
| RES-02 | P1 | TD | **[INV]** Resultados → listado. Abrir **S-AI** y **S-HUM** y descargar el JSON. | Listado paginado con código · condición · estado · fecha. INV ve la condición y las «operaciones degradadas registradas». El JSON está completo, sin contraseñas ni tokens, y con los estados en texto. | ☐ |

### Aislamiento e interfaz

**AIS-01 · P1 · TD — Matriz de acceso.** Sobre **S-HUM**, copiar desde P1 la URL de cada sección y abrirla en cada ventana.

| Sección | P2 (interlocutor) | P3 (tercero) | INV **[INV]** | Res. |
|---|---|---|---|---|
| Conversación | Ve y escribe (si Active) | No encontrada | Ve con la condición; no escribe | ☐ |
| Emociones | No encontrada | No encontrada | Ve; sin formulario | ☐ |
| Avatar | No encontrada | No encontrada | Ve | ☐ |
| Encuesta | Aviso «la responde el dueño» | No encontrada | Aviso «la responde el dueño» | ☐ |
| Resultados y JSON | No encontrada | No encontrada | Ve todo, con la condición | ☐ |

| ID | P | Téc. | Pasos | Esperado | Res. |
|---|---|---|---|---|---|
| AIS-02 | P1 | AE | Con P3, comparar lo que muestra la sesión de P1 frente a un GUID aleatorio. | Exactamente la misma respuesta: no se puede saber si existe una sesión ajena. | ☐ |
| UI-01 | P2 | CU | Recorrer el menú lateral completo. Abrir `/xyz`. Probar con 375, 768 y 1280 px de ancho. Recorrer login y Sesiones solo con teclado. Hacer el recorrido con la consola de DevTools abierta. | Ningún enlace falla y `/xyz` muestra «no encontrado». Sin scroll horizontal ni controles tapados. El foco es visible y todo se puede accionar con teclado. Sin errores en la consola. | ☐ |

---

## 3. Plan de tiempo (≈ 5 h por QA y ronda)

| # | Bloque | Min. |
|---|---|---:|
| 1 | Preparación | 30 |
| 2 | AUT-01 a AUT-05, ROL-01, ROL-02 | 48 |
| 3 | SES-01, SES-02 y arrancar TMP-01 y TMP-02 (sus esperas se solapan con los bloques siguientes) | 21 |
| 4 | EMP-01, EMP-02, CHU-01 a CHU-03 | 53 |
| 5 | CIA-01 a CIA-03 | 25 |
| 6 | EMO-01 a EMO-03 | 24 |
| 7 | ENC-01 a ENC-03, RES-01, RES-02 | 34 |
| 8 | SES-04, SES-05 | 13 |
| 9 | AIS-01, AIS-02, UI-01 | 30 |
| 10 | SES-03 (cierre con INV) | 6 |
| 11 | Registro de hallazgos | 25 |
| | **Total** | **309** |

- Es una estimación sin medir. Puntos de control a la 1,5 h, a las 3 h y a las 4,5 h.
- Si vas más de un 10 % retrasado, recorta los P2 en este orden: UI-01, CIA-03, EMO-03, EMO-02, SES-05, AUT-02 (≈ 40 min). Los P1 no se recortan.
- Ronda 2: el mismo plan. Dos rondas ≈ 10 h de las 22 h de cada QA.

---

## 4. Criterios de salida

- Todos los **P1** en ✅, o con su ❌ registrado.
- Ningún defecto abierto **crítico** o **alto** sobre: revelar la condición, acceso a datos ajenos, escalada de privilegios, pérdida de mensajes o de respuestas de encuesta.
- La lista de desarrollo (apartado 5) con su estado anotado.

**Severidad:** Crítica (fuga o pérdida de datos, seguridad, plataforma inutilizable) · Alta (flujo principal bloqueado) · Media (degradado, con alternativa) · Baja (cosmético).

---

## 5. Para desarrollo: verificaciones solo por API

La API de Azure solo es accesible desde dentro del entorno, así que QA no puede ejecutar estas verificaciones. Las ejecuta desarrollo con su sonda interna (o QA, si se habilita un acceso), por contrato HTTP.

| ID | Verificación | Esperado |
|---|---|---|
| DEV-01 | Registrar con rol `Administrator`; registrar con un rol desconocido | `403` y la cuenta **no** se crea; `400` con la lista de roles válidos |
| DEV-02 | Cambiar el rol de otra cuenta con token de Participant o Researcher | `403`; el rol no cambia |
| DEV-03 | `GET /api/results` y la cola de interlocutores con Participant; `GET /api/results` sin token | `403`; `401` |
| DEV-04 | Activar dos veces; completar una Created; activar, completar o cancelar una terminal; cancelar sin motivo | `409`; `409`; `409`; `400` |
| DEV-05 | El interlocutor envía con `sender: "Participant"`; el interlocutor envía antes de iniciar; un Researcher no asignado envía | `400`; `409`; `404`; ningún turno se guarda |
| DEV-06 | Mensaje de 4001 caracteres; intensidad emocional 1.5 o -0.1 | `400`; `400` |
| DEV-07 | Encuesta con valores inválidos; segundo envío; envío sobre una sesión Active | `400` sin respuestas parciales; `409`; `400` |
| DEV-08 | Crear sesiones con `preferredCondition: "Human"` repetidas veces, y con `"X"` | No todas salen Human; `400` |
| DEV-09 | Participant consulta su sesión, sus turnos, sus eventos y sus resultados | Sin condición, proveedor, modelo ni identidad del interlocutor |
| DEV-10 | Token alterado en cualquier ruta; revisar todas las respuestas | `401`; ninguna respuesta contiene contraseñas, hashes, tokens ni campos internos |

---

## 6. Registro de la ronda

| Campo | Valor |
|---|---|
| QA, ronda (1 o 2), fecha | |
| Horas reales (objetivo 5 h) | |
| Navegadores y versión; versión de la Web | |
| Tiempos confirmados (duración / inactividad) | |
| ¿Se promovieron INV y ADMIN? ¿Quién lo hizo? | |
| Casos: ✅ / ❌ / ⛔ / ➖ | / / / |
| Estado de DEV-01 a DEV-10 | |

**Plantilla de defecto:** `BUG-### · caso · título · severidad · URL, hora, navegador y cuenta · pasos · esperado (y de qué requisito sale) · obtenido · captura o vídeo · frecuencia`.

---

## 7. Trazabilidad

| Requisito (`openspec/specs`) | Casos |
|---|---|
| authentication | AUT-01 a AUT-05, ROL-01, ROL-02, DEV-01 a DEV-03, DEV-10 |
| session-lifecycle | SES-01, SES-02, SES-04, SES-05, TMP-01, TMP-02, DEV-04 |
| condition-assignment | SES-03, DEV-08 |
| conversation-turns | CIA-01, CIA-02, CHU-01, CHU-02, DEV-05, DEV-06 |
| emotion-readings | EMO-01, EMO-02, DEV-06 |
| avatar-expression | EMO-03 |
| survey | ENC-01 a ENC-03, DEV-07 |
| experiment-results | RES-01, RES-02, DEV-09 |
| realtime-events (actualización sin recargar) | CHU-01, CHU-02, SES-04, CIA-03 |
| web-experiment (cambio vigente) | EMP-01, EMP-02, CHU-01 a CHU-03, SES-05, UI-01 |
| Aislamiento por propietario | AIS-01, AIS-02, CHU-02 |
