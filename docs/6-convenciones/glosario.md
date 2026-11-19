# Glosario oficial del proyecto SUE

Propósito: Fijar el vocabulario oficial del proyecto: un término por cada idea, con sus equivalentes admitidos, para que toda la documentación (la que escriben las personas y la que escribe el agente) use siempre las mismas palabras.

Dirigido a: Todo el equipo del proyecto y, en especial, al agente que documenta, que debe escribir con estos términos.

Pauta: 6 – Convenciones técnicas. Ubicación: `docs/6-convenciones/glosario.md`.

Última actualización: 17 de noviembre de 2026 – Equipo de Documentación SUE.

## Contenido

### Cómo usar este glosario

1. **Un término por cada idea.** Al escribir se usa el término de la columna «Término oficial». Los «Equivalentes» solo se usan donde ya aparecen así (por ejemplo, en los nombres de archivo o en las pantallas de GitHub, que están en inglés).
2. **La palabra «migración» no se usa sola, nunca.** En este proyecto significa dos cosas distintas: siempre se escribe «migración de base de datos» o «migración de documentos».
3. **El nombre del proyecto.** En los documentos se llama SUE. Turning es su nombre técnico en el código y en el repositorio (`github.com/Leouninpahu1/ProyectoTuning`).
4. **Los términos técnicos en inglés se escriben igual que aquí** (pull request, merge, commit, smoke test…): no se traducen ni se inventan variantes.
5. **El agente escribe con este vocabulario.** Es una de sus reglas de operación: usar los términos del glosario para que todo quede escrito igual.
6. **El glosario está vivo.** Si aparece una palabra que no está aquí, se agrega con un pull request. Las cantidades que cambian con el tiempo (número de pruebas, de migraciones o de endpoints) no se escriben aquí: se consultan en el documento de cada pauta.

### Glosario

Está en orden alfabético. La primera columna es el término oficial; la segunda, los equivalentes con los que aparece en el proyecto.

| Término oficial | Equivalentes | Qué significa |
|---|---|---|
| Agente | ninguno | Asistente de inteligencia artificial que hace tareas siguiendo instrucciones; en SUE, el que documenta. |
| API | backend | La parte del sistema que recibe lo que pide la web, aplica las reglas del experimento y responde. El usuario no la ve. |
| API de emociones | emotions-api | Servicio aparte que analiza un texto o una imagen y devuelve la emoción detectada. SUE la usa para leer las emociones de la conversación. Está en `services/emotions-api/`. |
| Avatar | ninguno | La figura que muestra una expresión según las emociones detectadas en la conversación. |
| Base de datos | ninguno | Donde se guarda la información de forma permanente. SUE usa SQL Server. |
| Claude Code | ninguno | La herramienta de IA con la que trabaja el agente. Se usa con la licencia del semillero. |
| Clean Architecture | ninguno | Una forma de organizar el código en capas separadas, cada una con su función. |
| Código fuente | ninguno | Las instrucciones que escriben los programadores. Están en la carpeta `src/`. |
| Colección de Postman | ninguno | Un grupo de solicitudes guardadas para probar la API en orden. Está en `docs/3-api-e2e/` (colección de Turning API y su entorno); antes estaba en la carpeta `postman/`, que ya se eliminó. |
| Commit | ninguno | Un guardado de cambios con una nota, el autor y la fecha. |
| Condición | ninguno | Si el participante conversa con una persona (Human) o con la IA (AI). Se asigna sin que él lo sepa. |
| Dataset | ninguno | Un archivo con datos de prueba. En SUE hay uno con conversaciones inventadas, en `data/`. |
| Degradado | ninguno | Cuando la IA no está disponible y el sistema responde con reglas simples. Queda marcado así. |
| Despliegue | deploy (en nombres de archivo, p. ej. `deploy.md`) | Publicar el sistema en un servidor para usarlo por internet. SUE está publicado en Azure. |
| Diagrama ER | ninguno | Un dibujo de las tablas de la base de datos y de cómo se relacionan. |
| Diccionario de datos | ninguno | La descripción de cada tabla y cada campo de la base de datos. |
| E2E | flujo de punta a punta | De punta a punta: el recorrido completo, desde el registro hasta los resultados. |
| Encuesta | ninguno | El cuestionario que responde el participante al terminar la sesión, una sola vez; el investigador ve los resultados de todas. |
| Endpoint | ninguno | Cada función que ofrece la API, como iniciar sesión o enviar un mensaje. |
| Fork | ninguno | Una copia personal del repositorio hecha desde GitHub, para trabajar o proponer cambios sin afectar el original. |
| Git y GitHub | ninguno | Git guarda el historial de cambios; GitHub es la página web donde está el repositorio. |
| Interlocutor | ninguno | La persona o la IA con la que conversa el participante en una sesión; el participante no sabe cuál le tocó. |
| Markdown | ninguno | Un formato de texto sencillo para escribir documentos. Los archivos terminan en `.md`. |
| Merge | ninguno | Cuando se aprueba un PR y sus cambios pasan a la versión oficial (`main`). |
| Migración de base de datos | ninguno | Un cambio en la estructura de la base de datos, por ejemplo agregar un campo. |
| Migración de documentos | ninguno | Pasar un documento que ya existía a la carpeta de su pauta, sin perder su historial (se hace con `git mv`). |
| OpenSpec | ninguno | El método en el que primero se escribe lo que debe hacer el sistema (la especificación) y después se programa. Está en `openspec/`. |
| Pauta | ninguno | Cada una de las seis partes en que se organiza la documentación. |
| Prueba automática | test | Una revisión que se ejecuta sola para comprobar que algo funciona. Están en `tests/` y, para la API de emociones, en `services/emotions-api/tests/`. |
| Pull request (PR) | ninguno | La propuesta para que unos cambios entren a la versión oficial. Se revisa antes de aprobarse. |
| Rama | branch | Una copia de trabajo aparte. La versión oficial es la rama `main`. |
| README | ninguno | El archivo de presentación o índice de una carpeta. Cada carpeta de pauta tiene el suyo. |
| Repositorio | ninguno | El archivo completo del proyecto, con todo su historial. |
| Rol | ninguno | El tipo de usuario: participante, investigador o administrador. |
| Script | ninguno | Un programa pequeño que hace una tarea automáticamente. Están en `scripts/`. |
| Sesión | ninguno | Una conversación completa del experimento, con inicio, tiempo límite y cierre. |
| Skill | ninguno | Un archivo con instrucciones que el agente sigue siempre. |
| Smoke test | ninguno | Una prueba rápida para ver que el sistema arranca y responde. |
| SUE / Turning | ninguno | SUE es el nombre del proyecto; Turning es su nombre técnico en el código y en el repositorio. |
| Web | frontend | Las pantallas que usa la persona en el navegador. |

## Documentos relacionados

- [Índice general de la documentación](../README.md) — `docs/README.md`
- Guía de documentación del proyecto SUE (PDF compartido en el grupo) — fuente original del glosario (sección Glosario) y de las reglas de este documento
- Carpetas de las pautas 1 a 6 en `docs/` (cada una con su `README.md`)

> Nota de verificación: las definiciones provienen del glosario de la Guía de documentación del proyecto. Las ubicaciones (`docs/3-api-e2e/`, `openspec/`, `src/`, `tests/`, `data/`, `scripts/`) y el estado de los documentos se verificaron contra el repositorio el 17 de noviembre de 2026, con los PR #14 y #15 ya aprobados (la carpeta `postman/` ya se eliminó) y el PR #17, que migra la pauta 5, aún abierto.
