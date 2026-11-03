# Tasks

## 1. Application: token y servicio de restablecimiento

- [x] 1.1 Mover la longitud mínima de contraseña de `AuthService` a una constante compartida `MinimumPasswordLength = 8` y usarla en el registro; verificar que `AuthServiceTests` siguen pasando sin cambios.
- [x] 1.2 Añadir el puerto `IEmailSender` en `Application/Interfaces` y `PasswordResetOptions` (`BaseUrl`, `TokenMinutes = 30`); verificar que la solución compila.
- [x] 1.3 Implementar `PasswordResetService` (`RequestAsync`, `ResetAsync`): token HMAC con `Jwt:SigningKey` y prefijo `pwreset|`, huella del hash, `TimeProvider`, comparación en tiempo constante, enfriamiento de 60 s por correo con comentario `shortcut:` y fallos de envío absorbidos. Registrarlo en `AddApplicationServices` junto con `TimeProvider.System`. Verificar con `PasswordResetServiceTests` (NSubstitute, `TimeProvider` falso escrito en la prueba):
  - un correo vacío o mal formado falla con `AUTH_INVALID_REQUEST` y no envía nada;
  - un correo desconocido no envía nada;
  - un correo conocido envía un solo mensaje con el enlace a `BaseUrl`;
  - el enfriamiento evita el segundo correo;
  - el restablecimiento cambia el hash;
  - un token reutilizado, expirado, alterado o de un usuario inexistente falla con `AUTH_RESET_TOKEN_INVALID`;
  - una contraseña corta falla con `AUTH_INVALID_REQUEST` y el token sigue válido;
  - si el envío falla, no hay excepción y el mensaje de advertencia no contiene el token.

## 2. Infrastructure: envío de correo y configuración

- [x] 2.1 Implementar `SmtpEmailSender` con `System.Net.Mail` (STARTTLS, timeout de 10 s, credenciales por opciones `Email` y `PickupDirectory` para Development, creando esa carpeta si no existe), con el comentario `shortcut:` sobre OAuth2 y el puerto 465, y registrarlo en el contenedor de dependencias. Verificar con una prueba de Infrastructure que, con `PickupDirectory` en una carpeta temporal que todavía no existe, se crea la carpeta y se escribe un `.eml` con el destinatario, el asunto y el enlace.
- [x] 2.2 Validar al arrancar: fuera de Development, sin `Email:Smtp:Host` o sin `PasswordReset:BaseUrl`, registrar una advertencia sin fallar el arranque, siguiendo el patrón `Validate()` de `JwtOptions`. Verificar con pruebas de la validación (configuración completa, sin host y sin `BaseUrl`).
- [x] 2.3 Añadir a `appsettings.Development.json` `PasswordReset:BaseUrl = http://localhost:5251` y `Email:PickupDirectory`, sin secretos. Documentar las variables `PasswordReset__BaseUrl` y `Email__*` en `docs/configuracion-secretos.md` y `docs/deploy.md`:
  - que solo `Email__Smtp__Password` es secreto;
  - los valores para la cuenta Gmail dedicada (`smtp.gmail.com`, `587`, `Username` y `From` iguales a esa dirección);
  - cómo crear la contraseña de aplicación con la verificación en dos pasos activada;
  - que nunca se usa `proinvsemillero@gmail.com`.

  Verificar que los nombres documentados coinciden con las secciones de opciones.

## 3. API: endpoints anónimos

- [x] 3.1 Añadir a `AuthController` `POST /api/auth/forgot-password` (`[AllowAnonymous]`, responde `202` con `{}`) y `POST /api/auth/reset-password` (`[AllowAnonymous]`, responde `204`). Verificar con pruebas del controller (servicio sustituido) que ambos son anónimos y que `forgot-password` responde el mismo cuerpo exista o no el correo. Añadir `AUTH_RESET_TOKEN_INVALID → 400` a `ApiErrorContractTests`.

## 4. Web: botones y páginas

- [x] 4.1 Añadir `ForgotPasswordAsync` y `ResetPasswordAsync` a `AuthApiClient`, sin deserializar cuerpo en `202` y `204`. Verificar en `ApiClientTests` la ruta, el método y el cuerpo enviados, y que un `400 AUTH_RESET_TOKEN_INVALID` llega como error visible.
- [ ] 4.2 Añadir el botón visible «¿Olvidaste tu contraseña?» en `Login.razor`, debajo de «Iniciar mi viaje», y la página anónima `ForgotPassword.razor` (`EmptyLayout`, validación de correo, botón «Enviar enlace» y confirmación genérica que pide revisar el correo no deseado). Verificar en el navegador local que la confirmación es idéntica para un correo registrado y para uno inexistente.
- [ ] 4.3 Añadir la página anónima `ResetPassword.razor` (`?token=`, contraseña y confirmación con mínimo 8 caracteres, botón «Cambiar contraseña», éxito con enlace al login y enlace no válido con opción de pedir uno nuevo). Verificar en el navegador local los cuatro escenarios de la spec «Opciones de restablecimiento en la Web».
- [x] 4.4 Añadir a `docs/QA-MANUAL-SUE.md` un caso de caja negra de restablecimiento de contraseña: pedir el enlace, abrir el correo, cambiar la contraseña, entrar con la nueva y fallar con la anterior, y reusar el enlace. Verificar que el caso cita sus requisitos y que se actualizan los conteos del encabezado.

## 5. Integración y verificación

- [ ] 5.1 Ejecutar `dotnet build turning.sln` y `dotnet test turning.sln`; verificar que todo pasa.
- [ ] 5.2 Recorrido local con `./scripts/dev.ps1`: pedir el enlace en `/forgot-password`, abrir el `.eml` de `PickupDirectory`, cambiar la contraseña, entrar con la nueva, comprobar que la anterior falla y que reusar el enlace falla. Verificar además que `%LOCALAPPDATA%\Turning\logs\api.log` no contiene el token.
- [x] 5.3 Ejecutar `openspec validate add-password-reset-email --strict`; verificar que pasa.

## 6. Correcciones de la revisión de código

- [x] 6.1 Exigir una única dirección sin nombre visible en el registro y en la solicitud (`AuthService.IsPlainEmailAddress`), y exigirlo también en el adaptador antes de agregar el destinatario (`new MailAddress` acepta listas como nombre visible). Verificar con pruebas de registro y solicitud con listas y nombres visibles, y con la prueba del adaptador que rechaza una lista sin escribir el `.eml`.
- [x] 6.2 Quitar el nombre de la cuenta del cuerpo del correo y enviarlo sin esperar, con `SmtpEmailSender` singleton. Verificar que la prueba del correo conocido comprueba que el cuerpo no trae el nombre.
- [x] 6.3 Mover el enfriamiento a `PasswordResetCooldown` singleton con poda, liberar la reserva si el envío falla y registrar solo tipo de error y código SMTP. Verificar con la prueba de envío fallido (sin token, enlace ni correo en el log; el reintento inmediato envía) y la del enfriamiento liberado.
- [x] 6.4 Validar y cambiar el hash dentro de `SessionOperationLock` por usuario. Verificar con la prueba de dos restablecimientos simultáneos con el mismo token, en la que solo uno tiene éxito.
- [x] 6.5 Añadir el claim `pwd_stamp` al JWT y rechazarlo en `OnTokenValidated` si no coincide con la contraseña actual. Verificar con las pruebas del claim en el token y de validación (huella actual aceptada, antigua rechazada, ausente aceptada).
- [x] 6.6 En `ResetPassword.razor`, tratar solo un `400` como enlace no válido; resolver `PickupDirectory` relativo a la carpeta local de la aplicación; advertir en Production si `PickupDirectory` está activo. Verificar con la prueba de `Validate` con `PickupDirectory` y con la compilación de la Web.

## Workflow follow-up

- Probar en Azure solo después de que el equipo provea una cuenta de correo de servicio y configure sus secretos; el despliegue necesita su propia autorización.
- Archivar el cambio cuando se cumplan los requisitos de revisión del proyecto.
