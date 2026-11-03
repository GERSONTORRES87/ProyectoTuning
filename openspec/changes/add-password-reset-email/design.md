# Design

## Context

- `AuthController` expone solo `register`, `login`, `me` y la asignación de rol. `AuthService` valida la contraseña con una longitud mínima de 8 escrita en el propio método. El hash se fija al crear la cuenta (`UserAccount.Create`), y el único método que lo cambia en una cuenta existente es `UserAccount.UpdatePasswordHash`.
- No existe código de correo en `src`. `JwtOptions.SigningKey` (32+ caracteres, por entorno) es el único secreto de firma disponible en Application. En Development, si no se configura, `JwtAuthenticationExtensions` genera una clave efímera y la copia a las opciones con `PostConfigure`, así que el servicio también la recibe.
- La API corre con una sola réplica (mínimo 0, máximo 1) en Azure Container Apps, con ingreso interno detrás de la Web. Un reinicio en frío pierde todo lo que esté en memoria y en el disco del contenedor.
- `ExceptionHandlingMiddleware` traduce los códigos `AUTH_*` desconocidos a `400`, así que `AUTH_RESET_TOKEN_INVALID` no requiere tocar el middleware.
- `AuthApiClient.SendAsync<T>` espera un cuerpo JSON: una respuesta `202` sin cuerpo necesita un envío sin deserialización.
- Plan anterior: el handoff del 2026-10-23 («User Request - Password reset by email», variante A). Se reutiliza su diseño de token y se reemplaza el enlace en pantalla por el envío de correo.

## Goals / Non-Goals

**Goals:**
- Sin tabla, columna ni migración nueva: nada que aplicar sobre Azure SQL.
- Sin dependencias NuGet nuevas.
- Que se pueda probar en local sin un servidor de correo.

**Non-Goals:**
- Restablecimiento iniciado por un administrador y cambio de contraseña desde Configuración con la sesión iniciada.
- Límite de solicitudes por IP.
- Plantillas de correo con marca o en HTML enriquecido.

## Decisions

### Token sin estado firmado con HMAC

Formato: `base64url(userId | expiraUnix | HMACSHA256(clave, "pwreset|userId|expiraUnix|PasswordStamp"))`, con vigencia de 30 minutos y comparación con `CryptographicOperations.FixedTimeEquals`. `PasswordStamp` son los primeros 16 caracteres hexadecimales del SHA-256 del hash de contraseña.
- **Un solo uso** sin guardar nada: la huella cambia en cuanto cambia la contraseña.
- **Concurrencia:** `ResetAsync` valida y cambia el hash dentro de `SessionOperationLock` con clave en el id del usuario; sin él, dos peticiones con el mismo token validarían contra el hash viejo y pasarían las dos.
- **Clave:** `Jwt:SigningKey`, con el prefijo de propósito `pwreset|`. Las claves de ASP.NET Data Protection se perderían en cada reinicio en frío; una tabla de tokens obligaría a una migración.
- **Alternativas descartadas:** tabla `PasswordResetTokens` (migración en Azure, limpieza de tokens) y Data Protection persistido (otro secreto u otro almacenamiento).
- **Costo aceptado:** si se rota `Jwt:SigningKey`, los enlaces pendientes dejan de servir.

### `PasswordResetService` nuevo en Application

Expone `RequestAsync(email)` y `ResetAsync(token, newPassword)`. Es un servicio separado para no cambiar el constructor de `AuthService` ni sus pruebas.
- Recibe `TimeProvider`, que viene en el BCL, para probar la expiración sin esperar.
- La longitud mínima pasa a una constante compartida (`MinimumPasswordLength = 8`) que usan los dos servicios.
- El restablecimiento no inicia sesión: el usuario entra luego con la contraseña nueva.

### Puerto `IEmailSender` y adaptador SMTP con `System.Net.Mail`

- `IEmailSender.SendAsync(to, subject, textBody)` vive en `Application/Interfaces`.
- `SmtpEmailSender` en Infrastructure usa `SmtpClient` con STARTTLS (`EnableSsl`), un timeout de 10 s y credenciales por entorno.
- En Development, si `Email:PickupDirectory` está configurado, usa `SmtpDeliveryMethod.SpecifiedPickupDirectory` y escribe un `.eml` en esa carpeta. Una ruta relativa (`Turning/mail`) cuelga de la carpeta local de la aplicación: `%LOCALAPPDATA%` en Windows, `~/.local/share` en Linux. Así QA lo abre sin servidor de correo.
- **Un solo destinatario:** el adaptador exige `AuthService.IsPlainEmailAddress(to)` antes de agregarlo, porque `new MailAddress("a@x, b@y")` no falla: lo toma como nombre visible más una dirección. El registro también exige una única dirección sin nombre visible (`AuthService.IsPlainEmailAddress`), así que una cuenta no puede guardar `a@x.com, b@y.com` y repartir el enlace.
- **Sin texto del usuario en el correo:** el cuerpo no incluye el nombre de la cuenta, que elige quien se registra.
- **Remitente:** una cuenta Gmail dedicada a SUE. Gmail acepta SMTP por `smtp.gmail.com:587` con STARTTLS y una contraseña de aplicación (requiere verificación en dos pasos), que es justo lo que soporta `SmtpClient`. `From` debe ser la misma cuenta autenticada, porque Gmail reemplaza cualquier otro remitente.
- **Alternativa descartada:** MailKit, que suma una dependencia para un solo mensaje de texto. Se deja anotado como `shortcut:` en el adaptador: `SmtpClient` no soporta OAuth2 ni TLS implícito en el puerto 465; si Google retira las contraseñas de aplicación o se cambia a un proveedor que exija OAuth2, cambiar a MailKit.

### Envío sin esperar y fallos absorbidos

- `RequestAsync` dispara el envío y responde `202` sin esperarlo: si la respuesta tardara solo cuando la cuenta existe, el tiempo delataría qué correos están registrados.
- Lo que corre después de responder solo usa dependencias singleton (`IEmailSender`, `PasswordResetCooldown` y el log), por eso `SmtpEmailSender` se registra como singleton.
- Si el envío falla, libera la reserva del enfriamiento y registra una advertencia con el tipo de error y el código SMTP, sin el enlace ni el mensaje de la excepción (SMTP pone ahí la dirección del destinatario).
- **Alternativa descartada:** una cola con `BackgroundService`, que suma código y también pierde el correo si el proceso se reinicia en ese instante.

### Enfriamiento de 60 s por cuenta

- `PasswordResetCooldown`, singleton, guarda el correo normalizado y el instante de la reserva, y poda en cada reserva las entradas de más de un minuto.
- `shortcut:` es válido con una sola instancia, igual que `SessionOperationLock`. Si la API escala a más réplicas, mover el enfriamiento a la base o a un caché compartido.
- No se usa el limitador de ASP.NET porque todas las peticiones llegan con la IP del proxy de la Web.

### Huella de contraseña en el JWT

- `JwtTokenService` añade el claim `pwd_stamp` con la `PasswordStamp` de la cuenta. `OnTokenValidated`, que ya carga el usuario en cada petición, rechaza el token si la huella no coincide: las sesiones abiertas antes de un cambio de contraseña dejan de servir, sin columna ni migración.
- `shortcut:` un token sin el claim (emitido antes de este cambio) se acepta hasta que expire; pasados `Jwt:ExpirationMinutes` desde el despliegue, todos lo llevan.

### Configuración

- **`PasswordReset`:** `BaseUrl` (URL pública de la Web) y `TokenMinutes = 30`.
- **`Email`:** `From`, `Smtp:Host`, `Smtp:Port = 587`, `Smtp:Username`, `Smtp:Password` y `PickupDirectory`, este último solo para Development. Solo `Smtp:Password` es secreto.
- **Valores con Gmail:** `Host = smtp.gmail.com`, `Port = 587`, `Username` y `From` iguales a la dirección Gmail dedicada, y `Password` igual a la contraseña de aplicación de 16 caracteres, no la contraseña normal de la cuenta.
- Al arrancar fuera de Development sin `Email:Smtp:Host` o sin `PasswordReset:BaseUrl`, o con `Email:PickupDirectory` activo, la API registra una advertencia y no falla: el resto de la plataforma debe seguir funcionando aunque falte el correo.

### Web

- **Login:** `Login.razor` gana un botón visible «¿Olvidaste tu contraseña?» (estilo `btn-ghost`, debajo de «Iniciar mi viaje» y fuera del formulario) hacia `/forgot-password`.
- **Páginas nuevas:** `ForgotPassword.razor` y `ResetPassword.razor`, anónimas y con `EmptyLayout`, siguiendo el patrón de `Login` y `Register` (`EditForm` con validación por anotaciones).
- **Cliente:** `AuthApiClient` suma `ForgotPasswordAsync` y `ResetPasswordAsync`, que envían sin esperar cuerpo JSON.
- **Mensajes:** la confirmación es genérica y no promete que exista la cuenta. Solo un `400` se trata como enlace no válido (ofrece pedir uno nuevo); un error pasajero de red o de arranque deja el formulario para reintentar con el mismo enlace.

## Risks / Trade-offs

- **[Sin la cuenta Gmail configurada el flujo no entrega nada]** → La advertencia al arrancar lo deja visible. La aceptación en Azure queda bloqueada hasta que el equipo cree la cuenta dedicada y cargue su contraseña de aplicación; nunca se usará `proinvsemillero@gmail.com`.
- **[Cupo de Gmail de unos 500 destinatarios al día]** → Sobra para la demo y las rondas de QA. Si SUE crece, pasar a un proveedor transaccional cambiando solo la configuración.
- **[Google puede retirar las contraseñas de aplicación]** → El envío fallaría con un error de autenticación que se registra como advertencia. La salida es OAuth2 con MailKit (ver `shortcut:` del adaptador).
- **[Un correo puede perderse si el proceso se reinicia justo durante el envío]** → La respuesta ya fue `202`; el usuario no recibe nada y puede pedir otro enlace al minuto.
- **[Tokens de sesión sin `pwd_stamp` durante la transición]** → Los emitidos antes del despliegue sobreviven a un cambio de contraseña hasta expirar (`Jwt:ExpirationMinutes`, 120 min en Production).
- **[En Development la clave JWT es efímera si no se configura]** → Al reiniciar la API los enlaces pendientes dejan de servir; basta pedir uno nuevo, o fijar `Jwt:SigningKey` con `user-secrets`.
- **[El token viaja en la URL]** → Es de un solo uso, vence en 30 min y nunca se escribe en logs.
- **[Los correos pueden caer en no deseado]** → La confirmación de la Web pide revisar esa carpeta. La reputación del dominio depende del proveedor elegido.
- **[Rotar `Jwt:SigningKey` invalida los enlaces pendientes]** → Aceptable: basta pedir un enlace nuevo.

## Migration Plan

- No hay cambio de esquema.
- **Despliegue:** en Container Apps se carga `Email__Smtp__Password` como secreto, y `PasswordReset__BaseUrl`, `Email__From`, `Email__Smtp__Host`, `Email__Smtp__Port` y `Email__Smtp__Username` como variables de entorno normales. API y Web se publican juntas.
- **Reversión:** volver a las imágenes anteriores. No queda ningún dato que limpiar.

## Open Questions

- Nombre exacto de la cuenta Gmail dedicada. Solo cambia valores de configuración, no el diseño.
