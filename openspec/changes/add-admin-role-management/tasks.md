# Tasks

## 1. Listado de cuentas

- [x] 1.1 Añadir `IUserAccountRepository.SearchAsync(query, page, pageSize)` e implementarlo en `UserAccountRepository` (sin eliminadas, búsqueda en `NormalizedEmail` y `FullName`, orden por correo). Verificar que la solución compila.
- [x] 1.2 Añadir `IAuthService.ListUsersAsync(actorId, query, page, pageSize)`: exige Administrator (`AUTH_FORBIDDEN`), valida la paginación (`AUTH_INVALID_PAGE`) y devuelve `AuthenticatedUser` sin hash. Verificar con pruebas de `RoleAssignmentTests`: no administrador rechazado, paginación inválida rechazada, administrador recibe la página.
- [x] 1.3 Añadir `GET /api/auth/users` con `[Authorize(Roles = "Administrator")]` en `AuthController`. Verificar con una prueba de que el endpoint exige el rol Administrator.

## 2. Web

- [x] 2.1 Añadir `AuthApiClient.ListUsersAsync(query, page)`. Verificar en `ApiClientTests` la ruta, la consulta y la cabecera de autorización.
- [x] 2.2 Añadir en `Admin.razor` el segmento «Roles de usuario» dentro de `AuthorizeView Roles="Administrator"`: buscador, tabla, selector de rol, «Guardar» por fila con `SetRoleAsync`, paginación y avisos. Verificar que la Web compila; la verificación en navegador queda pendiente con la del restablecimiento.

## 3. Verificación

- [x] 3.1 Ejecutar las suites en el contenedor Linux y `openspec validate add-admin-role-management --strict`; verificar que pasan (salvo las pruebas que requieren LocalDB).
