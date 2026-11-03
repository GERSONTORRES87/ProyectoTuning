## MODIFIED Requirements

### Requirement: Registro de usuario

El sistema SHALL exponer `POST /api/auth/register` anónimo, exigiendo nombre, correo y contraseña. SHALL crear un Participant y devolver un JWT utilizable de inmediato; el cliente no decide privilegios.

#### Scenario: Registro exitoso
- **WHEN** se registra un correo nuevo con datos válidos
- **THEN** se persiste un Participant y se devuelve su JWT e identidad

#### Scenario: Correo ya registrado
- **WHEN** se registra un correo existente
- **THEN** se devuelve `409 Conflict`

#### Scenario: Datos inválidos
- **WHEN** falta nombre, correo o contraseña válida
- **THEN** se devuelve `400 Bad Request`

## REMOVED Requirements

### Requirement: El registro público concede el rol solicitado
**Reason**: Los privilegios deben distinguirse de la participación normal para que los roles sean funcionales.
**Migration**: El registro concede Participant. Las cuentas privilegiadas de desarrollo se inicializan con contraseñas utilizables y un administrador puede cambiar roles.

## ADDED Requirements

### Requirement: Concesión administrativa de rol

El sistema SHALL permitir que un Administrator asigne un rol reconocido a un usuario existente. SHALL rechazar al solicitante sin ese rol con `403` y conservar la identidad del usuario destinatario.

#### Scenario: Cambio autorizado
- **WHEN** un administrador asigna Researcher a un usuario existente
- **THEN** el usuario recibe ese rol para sus siguientes tokens

#### Scenario: Intento desde registro público
- **WHEN** un anónimo registra una cuenta solicitando Administrator
- **THEN** recibe `403` sin crear la cuenta; un registro sin rol privilegiado crea Participant

#### Scenario: Cambio sin privilegios
- **WHEN** un Participant intenta conceder un rol
- **THEN** recibe `403` y el rol destinatario no cambia
