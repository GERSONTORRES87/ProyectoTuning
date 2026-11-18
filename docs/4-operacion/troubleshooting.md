# Errores comunes y soluciones

Problemas que el equipo encontró al instalar, correr y probar el proyecto en Windows, y otros que se deducen del código y de la configuración, con su solución. Si encuentras uno nuevo, agrégalo aquí en un pull request.

## Clave JWT

### `RandomNumberGenerator no contiene ningún método denominado GetBytes`

Pasa al generar la clave JWT con este comando en Windows PowerShell 5.1 (el que trae Windows):

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

Ese atajo solo existe en PowerShell 7. Como el comando falla, la variable queda vacía y luego aparece el error de la sección siguiente. Esta forma funciona en las dos versiones:

```powershell
$b = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
$env:Jwt__SigningKey = [Convert]::ToBase64String($b)
"Largo de la clave: $($env:Jwt__SigningKey.Length)"
```

Debe mostrar 64. El mismo atajo aparece en [configuracion-secretos.md](../configuracion-secretos.md) y en el mensaje de error de la API; con PowerShell 5.1 hay que usar esta forma.

### `Configuracion JWT invalida en el entorno 'Production': Falta 'Jwt:SigningKey'`

Fuera de `Development`, la API no arranca sin la clave JWT. Pasa, por ejemplo, en el paso de migraciones del [smoke test](smoke-test.md), que arranca la aplicación en `Production`. Va seguido de `Unable to create a 'DbContext' of type 'TurningDbContext'` y `FALLO: dotnet ef database update`.

Solución: crear la clave en la misma ventana de PowerShell donde se va a correr el comando (ver la sección anterior). Las user-secrets no sirven en este caso, porque solo se cargan en `Development`. Para comprobar que la clave existe:

```powershell
"Largo de la clave: $($env:Jwt__SigningKey.Length)"
```

Si muestra 0, la clave no está en esa ventana. Una ventana nueva de PowerShell empieza sin ella.

### En Development, los tokens dejan de servir al reiniciar la API

Si no se configuró la clave, en `Development` la API arranca igual, pero con una clave temporal distinta en cada arranque, y avisa con un `WRN` en la consola. Se soluciona configurando la clave una vez con user-secrets, como explica [configuracion-secretos.md](../configuracion-secretos.md).

## Compilación y pruebas

### La compilación falla porque faltan los paquetes

El [smoke test](smoke-test.md) compila y prueba con `--no-restore`, así que no descarga los paquetes por su cuenta. En un clon nuevo, o después de que cambien las dependencias, hay que correr antes:

```powershell
dotnet restore
```

### Muchas advertencias amarillas al compilar

No son errores y no impiden compilar ni correr:

- `CS1591`: a una clase o método público le falta el comentario de documentación XML.
- `NU1902` y `NU1903`: dos paquetes tienen vulnerabilidades conocidas, `System.Security.Cryptography.Pkcs` 9.0.4 y `Microsoft.OpenApi` 2.4.1. Hay que actualizarlos, pero no bloquean el trabajo diario.

Un error de verdad sale en rojo y dice `error`.

## Git

### `git diff` o `git log` se quedan detenidos y no vuelve el `PS>`

Git abre la salida en un paginador. Se sale con la tecla `q`. Si se escribieron otras letras y aparece algo como `Examine:`, lo más rápido es cerrar esa ventana de PowerShell y abrir una nueva. Para evitarlo, se agrega `--no-pager`:

```powershell
git --no-pager diff
git --no-pager log --oneline -5
```

### `warning: in the working copy of '...', LF will be replaced by CRLF`

Es normal en Windows: Git avisa que ajusta los saltos de línea del archivo. No cambia el contenido y no hay que hacer nada.

## Herramientas

### Un programa recién instalado "no se reconoce como nombre de un cmdlet"

La ventana de PowerShell que ya estaba abierta no ve los cambios del PATH que hizo el instalador. Hay que cerrar la terminal y abrir una nueva. Mientras tanto, el programa se puede ejecutar con su ruta completa.

## Documentos relacionados

- [smoke-test.md](smoke-test.md): cómo correr la prueba rápida.
- [configuracion-secretos.md](../configuracion-secretos.md): clave JWT, base de datos y correo.
- [QUICKSTART.md](../../QUICKSTART.md): instalación y arranque.
