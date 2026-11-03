# Proposal

## Why

Hoy un usuario que olvida su contraseña no puede volver a entrar a SUE: la API solo ofrece registro, login, identidad y asignación de rol, y la Web no tiene ninguna opción de recuperación. Las cuentas de los QA y de los participantes se crean y se usan a diario, así que se necesita una recuperación autoservicio que envíe el enlace por correo.

Este cambio reemplaza la decisión del 2026-10-23 de mostrar el enlace en pantalla solo para la demo (variante A): ahora el enlace se envía por correo desde una cuenta dedicada al proyecto (una cuenta Gmail creada solo para SUE), nunca desde la cuenta personal de un integrante.

## What Changes

- **Botón «¿Olvidaste tu contraseña?»** en la pantalla de login, que abre una página anónima donde el usuario escribe su correo y pulsa «Enviar enlace».
- **API `POST /api/auth/forgot-password`** (anónima): si el correo pertenece a una cuenta, envía un correo con un enlace de restablecimiento válido por 30 minutos. La respuesta es la misma exista o no el correo.
- **Página de restablecimiento** `/reset-password?token=…` con nueva contraseña, confirmación y botón «Cambiar contraseña».
- **API `POST /api/auth/reset-password`** (anónima): valida el token y la contraseña nueva y la cambia. El token sirve una sola vez y queda inválido al expirar o al cambiar la contraseña.
- **Envío de correo** mediante un puerto en Application y un adaptador SMTP en Infrastructure, configurado por variables de entorno. En Development el correo se escribe como archivo `.eml` en una carpeta local, sin enviar nada.
- No hay cambios de esquema ni migraciones.

## Capabilities

### New Capabilities

Ninguna.

### Modified Capabilities

- `authentication`: se añaden los requisitos de solicitud de restablecimiento por correo, de restablecimiento con token de un solo uso y de envío seguro del correo. «Contraseñas no recuperables» se mantiene: no se recupera la contraseña, se reemplaza.

## Impact

- **API:** dos endpoints anónimos nuevos en `AuthController`.
- **Application:** `PasswordResetService` nuevo (token HMAC sin estado, enfriamiento por correo), puerto `IEmailSender`, opciones `PasswordReset` y constante compartida de longitud mínima de contraseña.
- **Infrastructure:** adaptador `SmtpEmailSender` con `System.Net.Mail` (sin dependencias nuevas), sus opciones `Email` y el registro en el contenedor de dependencias.
- **Web:** enlace en `Login.razor`, páginas `ForgotPassword.razor` y `ResetPassword.razor`, y dos métodos en `AuthApiClient`.
- **Configuración y secretos:** `PasswordReset__BaseUrl` (URL pública de la Web), `Email__From`, `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__Username` y `Email__Smtp__Password`. Solo `Email__Smtp__Password` es secreto; el resto son valores normales de entorno. Se documentan en `docs/configuracion-secretos.md` y `docs/deploy.md`, y las credenciales nunca van en el repositorio.
- **Dependencia externa:** el equipo crea una cuenta Gmail dedicada a SUE, con verificación en dos pasos y una contraseña de aplicación, y se envía por `smtp.gmail.com:587` con STARTTLS. **No** se usa `proinvsemillero@gmail.com`. Sin esa cuenta configurada el flujo responde igual, pero ningún correo llega.
- **Otros cambios en curso:** `restrict-role-assignment` y `complete-experiment-workflow` también tocan `authentication`; este cambio solo añade requisitos y no modifica los suyos.
