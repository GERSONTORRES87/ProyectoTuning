# Demo del experimento

1. Ejecutar `./scripts/dev.ps1` y abrir http://localhost:5251.
2. Registrar dos cuentas distintas: participante e interlocutor. Para probar ambos simultáneamente, usar perfiles de navegador distintos; también se pueden usar http://localhost:5251 y http://127.0.0.1:5251, que mantienen almacenamientos de autenticación separados.
3. En la cuenta participante abrir Sesiones y crear una sesión. Un investigador puede consultar la condición desde Resultados; el participante no debe verla.
4. En Human, compartir el código EXP-XXXXXXXX con la segunda cuenta. Esa cuenta usa «Unirme como interlocutor». El propietario inicia la sesión y ambos abren la conversación compartida.
5. Verificar que los mensajes enviados por cada identidad aparecen en la otra vista mediante polling. En AI, enviar dos mensajes y comprobar la respuesta del interlocutor; sin claves de proveedor se usa la respuesta por reglas.
6. Completar desde la cuenta propietaria. Enviar la encuesta y abrir resultados para comprobar las respuestas guardadas. Volver a Encuesta debe mostrar que ya fue enviada.
7. Iniciar sesión como researcher@turning.local / TurningDemo2026! para consultar sesiones de otros participantes. Un observador puede leer y no puede enviar mensajes ni cerrar esas sesiones.
8. Iniciar sesión como admin@turning.local / TurningDemo2026! para cancelar una sesión o cambiar el rol de una cuenta en Configuración. El usuario debe volver a autenticarse tras un cambio de rol.

Las cuentas privilegiadas anteriores existen exclusivamente en Development. El resto del entorno se configura mediante variables de entorno; no hay claves reales de proveedores en el repositorio.

## Evidencia reproducible

- `dotnet test turning.sln --no-restore` ejecuta pruebas de todas las capas y del cliente Web, incluyendo SQL Server LocalDB.
- `./scripts/verify-workflow.ps1` ejecuta el recorrido HTTP con dos identidades, ambas condiciones, concurrencia, permisos y encuesta definitiva.
- El script crea datos de prueba identificados con el prefijo `e2e-`; no borra datos de otros usuarios.
