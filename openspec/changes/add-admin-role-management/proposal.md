# Proposal

## Why

Para cambiar el rol de una cuenta, el administrador tiene que copiar su identificador desde el perfil de esa persona y pegarlo en Configuración: no hay forma de buscar ni de ver quién tiene qué rol. La página de Administración solo muestra emociones de sesiones activas.

## What Changes

- **API `GET /api/auth/users`**, solo para Administrator: lista paginada de cuentas (nombre, correo, rol e identificador), con búsqueda por nombre o correo. Nunca devuelve contraseñas ni hashes.
- **Segmento «Roles de usuario» en `/admin`**, visible solo para Administrator: buscador, tabla de cuentas con su rol actual, un selector de rol y un botón «Guardar» por fila. Reutiliza `PUT /api/auth/users/{id}/role`, que ya existe.
- El Researcher sigue viendo `/admin` (estadísticas) pero no el segmento de roles.
- Sin cambios de esquema.

## Capabilities

### New Capabilities

Ninguna.

### Modified Capabilities

- `authentication`: se añade el requisito de listado de cuentas para administración.

## Impact

- **Application:** `IAuthService.ListUsersAsync` y `IUserAccountRepository.SearchAsync`.
- **Infrastructure:** búsqueda paginada en `UserAccountRepository`.
- **API:** endpoint nuevo en `AuthController`.
- **Web:** `AuthApiClient.ListUsersAsync` y el segmento en `Admin.razor`.
- Se entrega en el mismo PR que `add-password-reset-email` (#13), a pedido del usuario.
