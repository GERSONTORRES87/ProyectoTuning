## ADDED Requirements

### Requirement: Listado de cuentas para administración

El sistema SHALL exponer `GET /api/auth/users`, solo para el rol `Administrator`, con una lista paginada de cuentas no eliminadas (identificador, nombre, correo y rol) ordenada por correo, que admite filtrar por un texto contenido en el nombre o el correo. La respuesta SHALL NOT incluir contraseñas ni hashes.

#### Scenario: Administrador lista cuentas

- **WHEN** un Administrator consulta el listado
- **THEN** la respuesta es `200 OK` con la página de cuentas, su total y el rol de cada una
- **AND** ninguna cuenta incluye contraseña ni hash

#### Scenario: Búsqueda por nombre o correo

- **WHEN** un Administrator consulta el listado con un texto de búsqueda
- **THEN** solo aparecen las cuentas cuyo nombre o correo lo contienen, sin distinguir mayúsculas

#### Scenario: Rol sin privilegio

- **WHEN** un Participant o un Researcher consulta el listado
- **THEN** la respuesta es `403 Forbidden`

#### Scenario: Paginación inválida

- **WHEN** se pide una página menor que 1 o un tamaño fuera de 1 a 50
- **THEN** la respuesta es `400 Bad Request`

### Requirement: Segmento de roles en Administración

La Web SHALL mostrar en `/admin`, solo al Administrator, un segmento «Roles de usuario» con buscador, la lista de cuentas con su rol actual y, por cada una, un selector de rol y un botón para guardarlo. Tras guardar SHALL avisar que la persona debe volver a iniciar sesión.

#### Scenario: Cambiar el rol desde Administración

- **WHEN** el Administrator elige otro rol para una cuenta y pulsa «Guardar»
- **THEN** el rol queda actualizado en la lista
- **AND** la página avisa que esa persona debe volver a iniciar sesión

#### Scenario: Investigador en Administración

- **WHEN** un Researcher abre `/admin`
- **THEN** ve las estadísticas pero no el segmento de roles

#### Scenario: Quitarse el propio rol de administrador

- **WHEN** el Administrator intenta cambiar su propia cuenta a otro rol
- **THEN** la página muestra el rechazo de la API y su rol no cambia
