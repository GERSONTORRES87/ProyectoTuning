# Clasificación de los documentos sin lugar — Borrador de trabajo

Propósito: Preparar la decisión del equipo de documentación sobre dónde va cada documento que hoy no tiene carpeta de pauta (sección 5.1 de la Guía de documentación del proyecto). No es la decisión final: el equipo de documentación marca la decisión en este borrador, la decisión final la aprueba el profesor y los cambios los sube el equipo de ingeniería.

Dirigido a: Equipo de Documentación SUE.

Pauta: Transversal (afecta principalmente a las pautas 3, 4 y 5).

Última actualización: 17 de noviembre de 2026 – Equipo de Documentación SUE.

## Contenido

### 1. Qué hay hoy

La ubicación de cada documento se verificó contra el repositorio el 17 de noviembre de 2026, con los PR #14 y #15 ya aprobados y aplicados, y el PR #17 (migración de la pauta 5) aún abierto. La columna «Propuesta de la Guía» es la que dejó el equipo de ingeniería en la sección 5.1 de la Guía de documentación; la «Recomendación» es este borrador, para discutir y marcar la decisión final.

| Documento | Ubicación actual | Propuesta de la Guía (5.1) | Recomendación | Decisión final |
|---|---|---|---|---|
| `deploy.md` | `docs/` (raíz) | Pauta 4 | Ya resuelto: enlazado desde la pauta 4, sin moverlo | ✓ |
| `checklist.md` | `docs/` (raíz) | Pauta 5 | Pauta 5 | ( ) |
| `QA-MANUAL-SUE.md` | `docs/` (raíz) | Pauta 5 o 4 | Pauta 5 | ( ) |
| `QA-RAPIDO-SUE.md` | `docs/` (raíz) | No aparece en la Guía | Pauta 5 | ( ) |
| `surveys-admin.md` | `docs/` (raíz) | Pauta 3 o guía de uso | Pauta 3 | ( ) |
| `DESARROLLO_GUIADO_POR_ESPECIFICACIONES_E_IA.md` | Raíz del repositorio | Pauta 5 | Pauta 5 | ( ) |
| `configuracion-secretos.md` | `docs/` (raíz) | No aparece en la Guía | Ya resuelto: enlazado desde la pauta 4, sin moverlo | ✓ |
| `DECISIONS.md` | Raíz del repositorio | No aparece en la Guía | Por confirmar con ingeniería; si va a la pauta 5, se enlaza sin moverlo | ( ) |
| `PLAN-MVP-TURNING-2026-REFINADO.md` | `docs/` (raíz) | Histórico | Histórico: no se borra, no entra en pautas | ( ) |
| `planes-individuales/` | `docs/` (raíz) | Histórico | Histórico: no se borra, no entra en pautas | ( ) |
| `tareas-claude.md` | `docs/` (raíz) | Histórico | Histórico: no se borra, no entra en pautas | ( ) |
| `CONTENIDO_POSTER_PROYECTO.md` | Raíz del repositorio | Histórico | Histórico: no se borra, no entra en pautas | ( ) |

### 2. Por qué cada recomendación

- **`deploy.md` y `configuracion-secretos.md` → ya quedaron enlazados desde la pauta 4, sin moverse.** La Guía proponía mover `deploy.md` a la pauta 4; ingeniería lo resolvió de otra forma equivalente: ambos quedaron en `docs/` (raíz), enlazados desde el README de la pauta 4, porque otros archivos los usan con esa ruta. Ya está aplicado en el repositorio.
- **`checklist.md` → pauta 5 (Proceso de desarrollo).** La Guía lo propone: es la lista de verificación antes de cada entrega, y la pauta 5 responde «cómo se trabaja y cómo se aportan cambios».
- **`QA-MANUAL-SUE.md` → pauta 5.** La Guía deja dos opciones (5 o 4). Este borrador recomienda 5 porque es el plan de pruebas que acompaña a las entregas, junto con `checklist.md`. Si el equipo prefiere verlo como operación, la alternativa es la pauta 4 junto a `deploy.md`: es la única fila con doble opción razonable y vale la pena discutirla. Como lo referencian `QA-RAPIDO-SUE.md` y un cambio de `openspec/`, lo más seguro es enlazarlo desde su pauta sin moverlo.
- **`QA-RAPIDO-SUE.md` → pauta 5.** No aparece en la propuesta de la Guía. Es la lista de verificación rápida de pruebas (unos 60 minutos, 10 casos vitales) que complementa a `QA-MANUAL-SUE.md`, el plan completo de unas 5 horas; por eso va en la misma pauta que él.
- **`surveys-admin.md` → pauta 3 (Flujo E2E).** La Guía deja «pauta 3 o una guía de uso». Este borrador recomienda 3 porque explica cómo consultar las encuestas, y la pauta 3 cubre las funciones de la API «desde el registro hasta los resultados». Si se decide que es material para el investigador más que del sistema, la alternativa es dejarlo como guía de uso fuera de las pautas.
- **`DESARROLLO_GUIADO_POR_ESPECIFICACIONES_E_IA.md` → pauta 5.** La Guía lo propone: describe la metodología de trabajo del proyecto, tema de la pauta 5.
- **`DECISIONS.md` → por confirmar con ingeniería.** No aparece en la Guía. Es un registro vivo de decisiones confirmadas (se agrega sin borrar; hoy está vacío) y parece hacer parte del flujo de trabajo de los agentes, así que conviene confirmar si es documentación de la pauta 5 o un archivo de apoyo que se queda en la raíz. En cualquier caso no se mueve: `scripts/handoff.ps1` y el skill `codex-claude-handoff` lo usan con su ruta actual.
- **Planes del semestre y póster → históricos.** La Guía lo propone: «no se borran, pero tampoco entran en las pautas». Pendiente de discutir si se agrupan (por ejemplo, en una carpeta de históricos dentro de `docs/`) o se quedan donde están, anotados en el índice general.

### 3. Lo que ya quedó resuelto en los pull requests

Con los PR #14 y #15 ya aprobados, estos documentos ya no están en discusión; quedaron así:

- **`ER.md` y `DIAGRAMA-ER-SQL-SERVER.md`**: quedaron en la pauta 2 (modelo de datos) con el PR #15, ya aprobado.
- **`docs/api/collection.json`**: el PR #14, ya aprobado, lo eliminó y lo reemplazó: la colección de Postman quedó en `docs/3-api-e2e/` (colección de Turning API y su entorno).
- **`demo.md`**: el PR de Juan lo llevó a la pauta 3 (ahora `docs/3-api-e2e/recorrido-demo.md`), ya aplicado.
- **`deploy.md` y `configuracion-secretos.md`**: quedaron en `docs/` (raíz), enlazados desde la pauta 4 sin moverse (PR de ingeniería), porque otros archivos los usan con esa ruta.
- **`sqlserver-migrations.sql`** salió de esta lista: era un script viejo que nadie usaba y se eliminó con aprobación del profesor.
- Pendiente: el PR #17, que migra la pauta 5, seguía abierto a la fecha de esta revisión; los documentos recomendados aquí a la pauta 5 se ubican cuando ese PR quede aprobado.

Los archivos de apoyo que no son documentación y no se mueven (la Guía indica enlazarlos sin moverlos): `scripts/` y `data/`.

### 4. Cómo se sube la decisión

1. El equipo de documentación marca la columna «Decisión final» de este borrador.
2. Se pasa el resultado al equipo de ingeniería, que sube los cambios con `git mv` en su propio PR (así ningún documento pierde el historial) y actualiza los índices (`docs/README.md` y el `README.md` de cada carpeta).
3. El profesor revisa y aprueba la decisión final.

## Documentos relacionados

- [Glosario oficial del proyecto SUE](6-convenciones/glosario.md)
- [Índice general de la documentación](README.md) — `docs/README.md`
- Guía de documentación del proyecto SUE (PDF compartido en el grupo) — secciones 5 y 5.1
