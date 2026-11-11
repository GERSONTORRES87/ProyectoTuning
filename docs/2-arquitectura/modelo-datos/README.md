# Modelo de datos

- [Diagrama ER](diagrama-er.md): diagrama entidad-relación del modelo persistente en SQL Server.
- [Diccionario de datos](diccionario-datos.md): detalle de cada tabla y columna.

Se enlazan, no se mueven:
- Script de inicialización: [scripts/db-init.sql](../../../scripts/db-init.sql). Se queda en `scripts/` con los demás scripts.
- Dataset sintético: [data/synthetic-v1.csv](../../../data/synthetic-v1.csv). Es un archivo de datos y se queda en `data/`. Desde el 23 de octubre el código ya no lo carga automáticamente (el seeder solo crea la encuesta y las cuentas demo); su uso actual está por confirmar.

Responsable: Gerson Torres (DBA).
