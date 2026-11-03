# Documentación — Turning

Organizada según las 6 pautas aprobadas por el profesor.

| Pauta | Carpeta | Responsable de migración |
|---|---|---|
| 1. Propósito y alcance | [1-proposito-y-alcance](1-proposito-y-alcance/) | Por definir |
| 2. Arquitectura y principios | [2-arquitectura](2-arquitectura/) | Gerson |
| 3. Flujo E2E completo | [3-api-e2e](3-api-e2e/) | Juan |
| 4. Guía de arranque y operación | [4-operacion](4-operacion/) | Gerson |
| 5. Proceso de desarrollo | [5-desarrollo](5-desarrollo/) | Juan |
| 6. Convenciones técnicas | [6-convenciones](6-convenciones/) | Por definir |

Reglas de la migración:
- Los scripts (`scripts/`) y el dataset (`data/`) no se mueven: se enlazan desde aquí, porque el código los usa con esa ruta.
- Los specs viven en `openspec/`: se enlazan, no se copian.
- Cada responsable mueve solo sus archivos, con `git mv`, en un PR propio.