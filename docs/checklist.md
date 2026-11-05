# Checklist de release — Turning

Usar antes de cada demo o entrega semanal. Marcar cada casilla con evidencia (log, captura o link al pipeline).

## 1. Build
- [*] `dotnet build turning.sln --no-restore` sin errores ni warnings nuevos
- [*] Solución abre sin proyectos rotos en el `.sln`

## 2. Tests
- [*] `dotnet test turning.sln --no-restore` en verde (Domain, Application, Infrastructure)
- [*] Pruebas de autorización por endpoint pasan (403/404 esperados donde aplica)
- [*] Prueba de aislamiento de sesiones (usuario A no ve sesión de B) en verde

## 3. Migración de base de datos
- [*] `dotnet ef database update` corre sin errores contra LocalDB
- [*] `scripts/db-reset.ps1` deja la base en un estado limpio y reproducible
- [*] Diagrama ER (`docs/2-arquitectura/modelo-datos/diagrama-er.md`) refleja el esquema actual

## 4. Seed / datos de prueba
- [*] Seed es idempotente (correrlo dos veces no duplica datos ni rompe)
- [*] Existen al menos 2 usuarios de prueba con roles distintos (Researcher/Administrator)

## 5. Camino feliz (demo)
- [*] `GET /api/health` responde `isHealthy: true`
- [*] Flujo completo reproducible sin intervención manual oculta:
      register → login → create session → activate → conversation-turns → complete → survey → results
- [*] Colección `docs/api/collection.json` importada y ejecutada de punta a punta sin errores

## 6. Documentación
- [*] README actualizado con pasos reales de instalación y ejecución
- [*] Comandos de validación documentados y probados por alguien externo al autor

## Go / No-Go
- [ ] Todas las casillas anteriores marcadas
- [ ] Firmado por: ______________________  Fecha: ______________
