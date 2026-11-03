# Configuración de secretos (JWT y base de datos)

**Desde 2026-09-05 la clave de firma JWT ya no está en `appsettings*.json`.**
Si acabas de hacer `pull` y la API te emite tokens que dejan de servir al
reiniciar, es esto: configúrala una vez con el comando de abajo.

## Por qué

`Jwt:SigningKey` estaba versionada en `src/turning.API/appsettings.json` y en
`appsettings.Development.json`. Cualquiera con acceso al repositorio (incluido su
historial) podía firmar tokens válidos para cualquier usuario y cualquier rol.
Las dos claves que estuvieron publicadas quedaron **revocadas**: la API las
rechaza aunque lleguen por variable de entorno.

## Cómo configurarla (desarrollo)

Recomendado — user-secrets, que guarda el valor fuera del árbol de trabajo
(en `%APPDATA%\Microsoft\UserSecrets\`), así no hay forma de commitearlo:

```powershell
# generar una clave nueva
$key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))

# guardarla para este proyecto
dotnet user-secrets --project src/turning.API set "Jwt:SigningKey" $key
```

Alternativa por variable de entorno (el doble guion bajo separa la sección):

```powershell
$env:Jwt__SigningKey = "<clave-de-al-menos-32-caracteres>"
```

Cada quien usa su propia clave en local; no hay que compartirla. Solo importa que
sea la misma dentro de un mismo despliegue, porque quien emite el token y quien
lo valida son el mismo proceso.

## Qué pasa si no la configuras

| Entorno | Comportamiento |
|---|---|
| `Development` | La API **arranca igual**, con una clave efímera distinta en cada arranque, y avisa con un `WRN` en consola. Los tokens emitidos dejan de valer al reiniciar. |
| Cualquier otro | La API **no arranca**: el mensaje de error indica qué falta y cómo configurarlo. |

Reglas que se validan al arrancar (`JwtOptions.Validate`): la clave debe existir,
tener al menos 32 caracteres (HMAC-SHA256 exige 256 bits) y no ser ninguna de las
claves revocadas; `Issuer`/`Audience` no pueden ir vacíos y `ExpirationMinutes`
debe ser positivo.

## Lo que sí sigue en appsettings

`Jwt:Issuer`, `Jwt:Audience` y `Jwt:ExpirationMinutes` no son secretos y siguen
versionados.

La connection string de desarrollo también sigue en `appsettings*.json`: apunta a
LocalDB con `Trusted_Connection=True`, o sea que no contiene credenciales. Para
apuntar a otro servidor, sobreescríbela por entorno en vez de editar el archivo:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=...;User Id=...;Password=..."
```

**Ninguna connection string con usuario y contraseña debe commitearse.**

## Claves de los proveedores de IA

La generación de texto usa una cadena de proveedores: **OpenAI → OpenRouter → adaptador por
reglas**. Cada uno necesita su clave, y como con la de firma del JWT, **nunca van en
`appsettings*.json`**:

```powershell
dotnet user-secrets --project src/turning.API set "Ai:OpenAi:ApiKey" "sk-..."
dotnet user-secrets --project src/turning.API set "Ai:OpenRouter:ApiKey" "sk-or-..."
```

O por variable de entorno:

```powershell
$env:Ai__OpenAi__ApiKey = "sk-..."
$env:Ai__OpenRouter__ApiKey = "sk-or-..."
```

Ojo con el nombre: la ruta usa `OpenAi` y `OpenRouter` tal como aparecen en la sección de
configuración, no `openai`/`openrouter`, que son los nombres con los que cada proveedor se
identifica en las respuestas y los eventos.

### Las claves ya están declaradas, con valor de marcador de posición

`appsettings.json` trae las dos variables con un valor evidente:

```json
"Ai": {
  "OpenAi":     { "ApiKey": "DUMMY-REEMPLAZAR-Ai__OpenAi__ApiKey" },
  "OpenRouter": { "ApiKey": "DUMMY-REEMPLAZAR-Ai__OpenRouter__ApiKey" }
}
```

Están ahí para que se vea qué hace falta y con qué nombre exacto, no porque sirvan. **El
código reconoce estos valores como marcadores de posición** (`AiProviderOptions.IsPlaceholderApiKey`)
y trata al proveedor como no disponible, igual que si la variable no existiera.

Eso importa: sin ese reconocimiento, una clave ficticia se tomaría por buena, el proveedor
entraría en la cadena, saldría a `api.openai.com`, recibiría un 401 y recién entonces se
saltaría. Una llamada de red inútil y su latencia en el primer turno de cada sesión.

Se reconocen, sin distinguir mayúsculas: cualquier valor que empiece por `dummy` o contenga
`reemplazar` o `placeholder`, y los literales `changeme`, `cambiar`, `pendiente`, `todo`,
`tbd`, `none`, `null`, `your-api-key`, `your-api-key-here`, `sk-xxx`, `sk-...`, `sk-or-xxx`.

Cuando llegue una clave real, **no se edita `appsettings.json`**: se sobreescribe por
user-secrets o entorno, como arriba. Los valores del archivo versionado se quedan como
están.

### La diferencia con `Jwt:SigningKey`

Que falte una clave de IA **no impide arrancar en ningún entorno**, y eso es deliberado: la
cadena está pensada para degradarse. Un proveedor sin clave se salta con un aviso al
arrancar, y si no queda ninguno responde el adaptador por reglas, con `degraded: true` en la
respuesta y un evento `DegradedOperation` en la sesión. Con `Jwt:SigningKey` es al revés:
sin ella no hay alternativa aceptable, así que fuera de Development la API no arranca.

Lo que sí rompe el arranque fuera de Development es una sección `Ai` mal formada —una URL
inválida, un timeout por intento mayor que el presupuesto total, o un `ProviderOrder` sin el
eslabón `rule-based`—, porque eso es un error de configuración y no una carencia prevista.

### Qué no es secreto

`BaseUrl`, `Model`, los timeouts y los umbrales del cortacircuitos están versionados en
`appsettings.json`: no son secretos y conviene que todo el equipo use los mismos.

## Correo del restablecimiento de contraseña

El enlace de «¿Olvidaste tu contraseña?» se envía desde una **cuenta Gmail dedicada a SUE**.
**Nunca** se usa `proinvsemillero@gmail.com` ni la cuenta personal de un integrante.

| Variable | Valor | Tipo |
|---|---|---|
| `PasswordReset__BaseUrl` | URL pública de la Web (en local, `http://localhost:5251`) | valor |
| `Email__From` | la dirección Gmail dedicada | valor |
| `Email__Smtp__Host` | `smtp.gmail.com` | valor |
| `Email__Smtp__Port` | `587` (STARTTLS) | valor |
| `Email__Smtp__Username` | la misma dirección Gmail dedicada | valor |
| `Email__Smtp__Password` | la contraseña de aplicación de esa cuenta | **secreto** |

### Contraseña de aplicación de Gmail

1. En la cuenta dedicada, activa la **verificación en dos pasos** (Cuenta de Google → Seguridad).
2. En *Seguridad → Contraseñas de aplicaciones*, crea una llamada «SUE API».
3. Copia la clave de 16 caracteres: es `Email__Smtp__Password`, no la contraseña normal de la cuenta.

En desarrollo se guarda fuera del repositorio:

```powershell
dotnet user-secrets --project src/turning.API set "Email:Smtp:Password" "<contraseña-de-aplicación>"
```

`From` y `Username` deben ser la misma dirección: Gmail reemplaza cualquier otro remitente.

### Sin servidor de correo en local

`appsettings.Development.json` define `Email:PickupDirectory` como `Turning/mail`, una ruta
relativa a la carpeta local de la aplicación. Con esa clave el correo no se envía: se escribe como
`.eml` en `%LOCALAPPDATA%\Turning\mail` (en Linux, `~/.local/share/Turning/mail`) y se abre con cualquier cliente
de correo. Para probar el envío real con Gmail en local, deja `Email__PickupDirectory` vacío y
configura las variables de la tabla.

Fuera de Development, si falta `Email:Smtp:Host` o `PasswordReset:BaseUrl`, la API arranca igual
y registra una advertencia: el resto de la plataforma funciona, pero no llega ningún correo.

## Pendiente

La clave que estuvo en el historial de git seguirá siendo visible ahí. Como nunca
se usó fuera de desarrollo local, no hay que rotar nada en producción — pero si
algún despliegue llegó a usarla, hay que rotar la clave y invalidar los tokens
emitidos.
