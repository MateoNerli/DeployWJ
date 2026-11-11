# DeployWebJobs v2

Herramienta (en versión **con ventanas** y en versión **de consola**) para **publicar WebJobs en los App Services de Azure de todos los clientes**, sin FTP, sin contraseñas en texto plano y con un asistente paso a paso.

Elegís la carpeta que ya publicaste, elegís el WebJob, tildás los clientes y publicás. Ves el avance de cada cliente en vivo y, si algo falla, una pantalla aparte con el detalle y la opción de reintentar solo los que fallaron.

---

## Uso rápido (primera vez)

1. **Generá los ejecutables** (ver [Cómo generar el .exe](#cómo-generar-el-exe)) o corrí directamente `dotnet run --project src\DeployWebJobs.Ui` (ventanas) / `dotnet run --project src\DeployWebJobs.Cli` (consola).
2. **Menú Clientes → "Importar desde el Excel anterior".** Pasa los clientes del `ConexionesFTP.xlsx` a la configuración y las contraseñas al Administrador de credenciales de Windows.
3. **Menú Publicar → elegí tu carpeta → elegí "Simulación".** Muestra qué haría sin conectarse a nada.
4. **Publicá de verdad en UN cliente de prueba.** Así confirmás que ese sitio acepta la autenticación básica de SCM.
5. Después publicá al resto.
6. Cuando verifiques que todo anda, **borrá `ConexionesFTP.xlsx`** (tiene las contraseñas en texto plano).

---

## Versión con ventanas (`DeployWebJobsUI.exe`)

Hace lo mismo que la consola, con la misma lógica (proyecto Core), pero con ventanas (Avalonia). Menú lateral con **Publicar · Clientes · WebJobs · Ajustes · ¿Cómo funciona?**.

**Publicar** es una sola pantalla con cuatro bloques:

1. **Carpeta de publicación:** *Elegir carpeta…* (o pegás la ruta) y *Analizar*. Muestra cuántos archivos se suben, cuántos se excluyen, avisos y la lista de archivos.
2. **WebJob:** elegís uno que ya usaste o escribís el nombre (la carpeta sugiere uno). Continuo o Triggered. Y marcás si **ya existe en Azure** o **es nuevo** (ver abajo).
3. **Clientes:** casillas por cliente, buscador, *Marcar todos* / *Quitar marcas*. Los deshabilitados o sin credenciales aparecen grisados con el motivo.
4. **Publicar:** *Simulación*, *En paralelo, de a N*, y el botón **Publicar** (pide confirmación si está activada en Ajustes).

Mientras publica ves una barra general y una barra por cliente con el paso actual, los bytes subidos y el tiempo. **Cancelar** corta de forma ordenada. Al terminar aparece el **resumen** (publicadas / con errores) y, si hubo errores, una **pantalla aparte con el detalle** y el botón **Reintentar solo los que fallaron**.

**Ya existe / Es nuevo (evita errores de nombre).** Antes de subir, la herramienta consulta a Azure qué WebJobs tiene cada cliente:

- *Ya existe en Azure (actualizar)*: el nombre tiene que coincidir **exacto** (mayúsculas, puntos y guiones incluidos). Si no coincide, a ese cliente no se le sube nada y el error dice qué nombres parecidos existen (por ejemplo `¿Será 'WJSGR.Foo'?`).
- *Es nuevo*: si ya existe uno con ese nombre (aunque cambie una mayúscula), tampoco se sube nada, para no pisarlo.

Un cliente que no cumple no frena a los demás. La verificación se hace contra Azure, así que la **Simulación** solo la anota en el plan (no se conecta).

**Clientes** permite agregar, editar, quitar e importar desde el Excel anterior; la contraseña va directo al Administrador de credenciales de Windows.

### Pasarle la herramienta a un compañero

Pasale **solo el `DeployWebJobsUI.exe`** (no necesita instalar .NET) y un paquete de clientes:

1. En tu PC: **Clientes → Exportar para compañeros…**. Elegís si incluye las contraseñas. Si las incluye, quedan **cifradas** (AES-256-GCM, clave derivada con PBKDF2) con una contraseña del paquete que elegís vos; sin ella no se pueden leer. Se guarda un archivo `.dwjclients`.
2. Pasale el `.exe` y el `.dwjclients` (dejalos en la misma carpeta). **La contraseña del paquete pasásela por otro medio** (por ejemplo, de palabra), no junto con el archivo.
3. En la PC del compañero: al abrir el programa por primera vez, si no tiene clientes y encuentra el `.dwjclients` junto al `.exe`, ofrece cargarlo. También se puede hacer a mano con **Clientes → Importar paquete…**. Las credenciales quedan en el Administrador de credenciales de *su* usuario de Windows.

Sin contraseñas: el compañero ve los clientes marcados como "Faltan credenciales" y las carga en Clientes → Editar (salen del Azure Portal: App Service → Centro de implementación → Credenciales locales). El paquete no incluye rutas locales de tu PC.

---

## Cómo se usa (consola)

**Menú principal:** Publicar un WebJob · Clientes · WebJobs conocidos · Ajustes · ¿Cómo funciona? · Salir.

### Publicar un WebJob

| Paso | Qué hacés |
|---|---|
| 1. Carpeta | Pegás la ruta (o arrastrás la carpeta a la ventana) de la **carpeta ya publicada**, por ejemplo `...\bin\Release\net472\publish`. Te sugiere la última usada. |
| 2. WebJob | Elegís uno de los conocidos (hay búsqueda), el que sugiere la carpeta (toma el nombre que está antes de `bin`), o escribís otro. No hace falta registrarlo antes: se agrega solo la primera vez que publicás. |
| 3. Archivos | Ves qué se va a subir, qué se excluye y los avisos (por ejemplo, si no hay ningún ejecutable). |
| 4. Clientes | Todos los habilitados, algunos (casillas) o solo uno. |
| 5. Opciones | Publicar de verdad o **simulación**; en paralelo (hasta N a la vez) o uno por uno. |

Antes de publicar de verdad se muestra un **resumen y se pide confirmación** (por defecto, "no").

Durante la publicación ves, por cliente: el paso actual (Conectando, Deteniendo, Subiendo con bytes, Iniciando, Verificando), una barra de progreso y el tiempo. **Ctrl+C cancela de forma ordenada:** lo ya subido queda subido y lo pendiente no se toca.

Al terminar aparece la tabla de resultados (OK / Error / Intentos / Tiempo). Si hubo errores podés **ver el detalle** o **reintentar solo los que fallaron**.

### Cómo agregar un cliente

Menú **Clientes → Agregar un cliente**. Te pide:

- **Nombre** del cliente (el que ves en las listas).
- **Nombre del App Service** en Azure (ej. `miempresa-sgr`). Se usa para armar `https://<sitio>.scm.azurewebsites.net`.
- **Host SCM** (opcional): solo si el sitio no usa el host estándar.
- **Usuario y contraseña de publicación:** salen del Azure Portal, *App Service → Centro de implementación → Credenciales locales / FTPS* (el usuario empieza con `$`).

La contraseña va directo al Administrador de credenciales de Windows; nunca se escribe en el JSON. En el mismo menú podés editar (nombre, sitio, host SCM, habilitar/deshabilitar, credenciales) y eliminar clientes.

> Si preferís editar el JSON a mano, agregá el cliente en `config.json` (ver [formato](#configuración)) y cargá sus credenciales desde **Clientes → Editar → Cargar o cambiar las credenciales**.

### Cómo se arma el paquete que se sube

La herramienta **no compila**: sube lo que ya compilaste. Antes de usarla generás la carpeta de publicación del WebJob:

- Visual Studio: clic derecho en el proyecto → **Publicar → Carpeta**.
- Línea de comandos (proyectos de estilo SDK): `dotnet publish -c Release`.

Esa carpeta (`.exe`, `.exe.config`, DLL de las que depende, `appsettings.json`, subcarpetas) es lo que necesita el WebJob para correr en Azure. Qué hace la herramienta con ella:

1. Recorre la carpeta (sin seguir enlaces simbólicos) y descarta lo que coincide con los **patrones excluidos** (Ajustes). Por defecto: `appsettings.Development.json`, `*.Development.json`, `*.vshost.exe`, `*.vshost.exe.config`, `*.nupkg`, `*.zip`, `.vs/**`, `.git/**`.
2. Valida que cada ruta quede **dentro** de la carpeta elegida (protección contra `..\..\`).
3. Comprime todo en **un ZIP temporal** (una sola vez para todos los clientes) y lo borra al terminar.

> Los `.pdb` **sí se suben** (igual que la versión anterior), porque dan números de línea en los errores de producción. Si no los querés, agregá `*.pdb` en Ajustes → Archivos que NO se suben.

### Qué pasa con cada cliente

1. **Validando configuración:** cliente habilitado y con credenciales.
2. **Conectando:** `GET /api/continuouswebjobs` en Kudu por HTTPS; prueba el sitio y las credenciales.
3. **Deteniendo** el WebJob (solo los continuos) para liberar el `.exe`. Si todavía no existe, sigue.
4. **Subiendo:** `PUT /api/zip/site/wwwroot/app_data/jobs/continuous/<WebJob>/` con el ZIP. **Sobrescribe archivos pero no borra los que ya estaban.**
5. **Iniciando** el WebJob.
6. **Verificando** que el estado quede en `Running`.
7. **Resultado.**

Ante errores de red, timeouts o 5xx se reintenta solo (espera creciente: 2 s, 4 s, 8 s...). Los errores definitivos (401, 403, 404) no se reintentan.

---

## Cómo generar el .exe

Requisito: **.NET SDK 10** (solo en la máquina donde generás el `.exe`; quien lo use no necesita .NET).

```powershell
cd C:\DEV\DEV-UTILS\DeployWebJobs\v2
.\build.ps1
```

Corre los tests y deja en `.\dist\` dos archivos: `DeployWebJobs.exe` (consola) y `DeployWebJobsUI.exe` (ventanas); cada uno es un único archivo, self-contained, win-x64, con ícono y versión. Variantes:

```powershell
.\build.ps1 -SkipTests                 # sin correr los tests
.\build.ps1 -Only Ui                   # solo la versión con ventanas (Cli = solo consola)
.\build.ps1 -OutputDir D:\Herramientas # otra carpeta de salida
```

El script ejecuta, en esencia:

```powershell
dotnet publish src\DeployWebJobs.Cli\DeployWebJobs.Cli.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# y lo mismo con src\DeployWebJobs.Ui\DeployWebJobs.Ui.csproj para DeployWebJobsUI.exe
```

- **Versión:** se define en `Directory.Build.props` (`<Version>`). Se ve en el encabezado de la app, con `DeployWebJobs.exe --version` y en *Propiedades → Detalles* del `.exe`.
- **Ícono:** `src\DeployWebJobs.Cli\Assets\app.ico` (la versión con ventanas usa una copia en `src\DeployWebJobs.Ui\Assets`). Se regenera con `python tools\make-icon.py` (sin dependencias).
- El primer arranque de un `.exe` single-file puede tardar un poco más (se autoextrae). Si Windows SmartScreen avisa de un editor desconocido, es porque el `.exe` no está firmado; firmarlo con un certificado de código lo evita.

---

## Dónde se guarda todo

| Qué | Dónde |
|---|---|
| Configuración (clientes, WebJobs, ajustes; **sin secretos**) | `%AppData%\DeployWebJobs\config.json` |
| Contraseñas | Administrador de credenciales de Windows → Credenciales de Windows → `DeployWebJobs/<id-del-cliente>` |
| Logs (un archivo por día, 30 días) | `%AppData%\DeployWebJobs\logs\deploywebjobs-AAAAMMDD.log` |

### Configuración

```json
{
  "version": 1,
  "settings": {
    "maxParallelism": 3,
    "maxRetries": 3,
    "retryBaseDelaySeconds": 2,
    "requestTimeoutSeconds": 60,
    "uploadTimeoutSeconds": 300,
    "verifyTimeoutSeconds": 30,
    "confirmBeforeDeploy": true,
    "excludePatterns": ["appsettings.Development.json", "*.Development.json", ".vs/**"]
  },
  "clients": [
    { "id": "aval-oc", "name": "Aval OC", "enabled": true, "siteName": "aval-oc", "method": "ZipDeploy" }
  ],
  "webJobs": [
    { "name": "Webjob.CentralDeConsultaSql", "type": "Continuous", "lastFolder": "C:\\...\\publish", "lastDeployedUtc": "2026-10-27T15:00:00Z" }
  ]
}
```

Todo esto se puede cambiar desde la app (Clientes, WebJobs conocidos, Ajustes). Si necesitás un host SCM distinto del estándar, agregá `"scmHost": "mi-sitio-abc.scm.azurewebsites.net"` al cliente.

---

## Seguridad

- **Contraseñas** cifradas por Windows (Credential Manager), atadas al usuario de Windows. Nunca en el JSON, en pantalla ni en logs.
- **Solo HTTPS** hacia Kudu, con validación normal de certificado (la versión anterior aceptaba cualquier certificado).
- **Validación de rutas:** nada fuera de la carpeta de publicación entra al paquete; no se siguen enlaces simbólicos; el nombre del WebJob solo admite letras, números, `.`, `-` y `_`.
- **Confirmación** antes de publicar de verdad y modo **simulación** para revisar antes.

---

## Cómo está armado

```
v2/
├── src/
│   ├── DeployWebJobs.Core/      Lógica de deploy, SIN interfaz (compartida por la consola y la versión con ventanas)
│   │   ├── Models/              ClientConfig, WebJobDef, AppConfig, enums
│   │   ├── Configuration/       ConfigService (config.json)
│   │   ├── Security/            ICredentialStore, WindowsCredentialStore, PathGuard
│   │   ├── Packaging/           PackageBuilder, GlobMatcher, WebJobNameGuesser
│   │   ├── Deploy/              IDeployStrategy, ZipDeployStrategy, DeployOrchestrator, RetryPolicy
│   │   ├── Import/              ExcelClientImporter (Excel anterior), ClientPackage (.dwjclients para compañeros)
│   │   └── DependencyInjection/ AddDeployWebJobsCore(): registra todo el Core
│   ├── DeployWebJobs.Cli/       Consola con Spectre.Console + inyección de dependencias + Serilog
│   └── DeployWebJobs.Ui/        Ventanas con Avalonia (MVVM): ViewModels/, Views/, Services/ (diálogos)
├── tests/DeployWebJobs.Tests/   xUnit
├── tools/make-icon.py
└── build.ps1
```

- **`IDeployStrategy`** es la forma de subir el paquete. Hoy existe `ZipDeployStrategy` (Kudu); agregar otra (por ejemplo FTPS) es implementar la interfaz y registrarla, sin tocar el resto.
- **El orquestador no imprime nada:** informa por `IProgress<DeployProgress>`. Por eso la misma lógica sirve para la consola y para la versión con ventanas. En esta última, los eventos de progreso se juntan en un diccionario (el último por cliente) y un timer de ~100 ms los vuelca a la pantalla, así la interfaz no se traba aunque haya muchos clientes en paralelo.
- Todo es `async` con `CancellationToken`.

### Tests

```powershell
dotnet test DeployWebJobs.slnx
```

Cubren: patrones de exclusión, validación de rutas, armado del ZIP, configuración (sin secretos en el JSON), reintentos, el flujo Kudu completo con un servidor simulado (orden de llamadas, autenticación, 401/503, verificación), el orquestador (paralelismo, errores aislados, simulación, cancelación, evento final por cliente) y la importación del Excel.

---

## Cambios respecto de la versión original

| Antes (`DeployWebJobsMasivo`) | Ahora (v2) |
|---|---|
| .NET Framework 4.7.2, un solo `Program.cs` de ~190 líneas | .NET 10, solución Core + Cli + Tests con inyección de dependencias |
| Rutas y nombre del WebJob fijos en el código (había que recompilar para cambiar de WebJob) | Carpeta y WebJob se eligen en cada ejecución; hay una lista de WebJobs conocidos |
| Clientes y **contraseñas en un Excel** (versionado en git) | `config.json` sin secretos + Administrador de credenciales de Windows; alta/edición/baja desde la app |
| FTP/FTPS con `e.Accept = true` (aceptaba **cualquier certificado**) | Zip Deploy por HTTPS (Kudu) con validación de certificado; método intercambiable con `IDeployStrategy` |
| Subía solo `*.exe`, `*.exe.config` y `*.pdb` del primer nivel | Sube **toda** la carpeta publicada (con exclusiones configurables) |
| Subía sobre un WebJob en ejecución (archivos bloqueados) | Detiene el WebJob, sube, lo inicia y **verifica** que quede `Running` |
| Una celda vacía del Excel tiraba abajo todo el proceso | Un error en un cliente nunca frena a los demás |
| Sin reintentos, sin timeouts, sin cancelación | Reintentos con espera creciente, timeouts, Ctrl+C ordenado |
| Secuencial, sin confirmación | Paralelo con límite o secuencial; confirmación antes de publicar; **simulación (dry-run)** |
| Mensajes sueltos por consola, sin logs | Consola Spectre.Console **y** versión con ventanas (Avalonia): progreso por cliente, resultados, pantalla de errores, reintentar fallidos; logs Serilog por día |
| EPPlus con licencia "no comercial" declarada | Sin EPPlus; solo se lee el Excel una vez para importar (ClosedXML, MIT) |
| Sin tests, exit code siempre 0 | Más de 80 tests xUnit |
| Compilado como .NET Framework | `.exe` único self-contained win-x64 con ícono y versión (`build.ps1`) |

---

## Solución de problemas

| Mensaje | Qué significa |
|---|---|
| `HTTP 401` | Usuario o contraseña de publicación incorrectos. Cargalos de nuevo en Clientes → Editar. |
| `HTTP 403` | La autenticación básica de SCM está deshabilitada en ese sitio, o hay restricción de IP. |
| `HTTP 404` | Nombre de sitio o host SCM incorrecto. |
| `Tiempo de espera agotado` | Problema de red o paquete grande; se reintenta solo. Podés subir el tiempo máximo en Ajustes. |
| `no quedó en ejecución (estado: ...)` | El paquete se subió pero el WebJob no arrancó: revisá los logs del WebJob en el portal. |
| `No hay credenciales guardadas` | El cliente no tiene contraseña en el Administrador de credenciales (otra PC o usuario de Windows). Cargala en Clientes → Editar. |

Los detalles técnicos de cada ejecución quedan en el log del día.

---

## Pendiente / próximos pasos

- **Seguridad del repositorio:** el `ConexionesFTP.xlsx` anterior sigue en el historial de git. Conviene rotar las contraseñas de publicación de los sitios y purgar el historial.
- Estrategia **FTPS** (si algún cliente lo necesita) y autenticación con **Microsoft Entra ID** en lugar de las credenciales de publicación.
#   D e p l o y W J  
 