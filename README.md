# Claude Multi-Account

Abre **el Claude Desktop oficial** varias veces a la vez en Windows, una por cuenta, cada una con sus propios datos.
Por ejemplo, tu cuenta personal y la del trabajo, abiertas juntas y sin mezclarse.

No es un Claude nuevo ni una copia: lanza el mismo programa de Anthropic con una carpeta de datos distinta
(`--user-data-dir`) y una configuración de Claude Code distinta (`CLAUDE_CONFIG_DIR`). Cada perfil tiene todas las
funciones del original (chat, Code, Cowork, conectores…) y se actualiza con él.

> **No es una herramienta oficial de Anthropic** ni está relacionada con ella. «Claude» es una marca de Anthropic.
> La herramienta se apoya en un comportamiento no documentado de Claude Desktop que puede cambiar con cualquier
> actualización.

**Versión 1.1.0** · Windows 11 de 64 bits · Claude Desktop instalado desde la Microsoft Store o el instalador
oficial (MSIX) · probada con Claude Desktop 4.29.0.0.

## Instalar

1. Descarga `ClaudeMultiAccount-<versión>-win-x64.zip` de la página **Releases** de este repositorio.
2. Haz clic derecho en el zip → **Extraer todo…** (no lo abras sin extraer).
3. Abre `ClaudeMA.exe` de la carpeta extraída. Funciona así, como portátil.
4. Si quieres tenerla en el menú Inicio, pulsa **«Instalar en este equipo…»**: se instala solo para tu usuario, sin
   permisos de administrador.

El programa no está firmado, así que Windows (SmartScreen) puede avisar la primera vez: «Más información» →
«Ejecutar de todas formas». Junto al zip hay un archivo `.sha256` para comprobar que la descarga está intacta.

La guía completa, paso a paso, está en [docs/GUIA-DE-USO.md](docs/GUIA-DE-USO.md) (también va dentro del zip).

## Cómo se usa

- La primera vez, la lista empieza con tu Claude de siempre (el **perfil principal**, con sus datos de siempre).
- **«Nuevo perfil»** crea otro Claude con sus propias carpetas. La primera vez que lo abres, un asistente te guía
  para iniciar sesión con la otra cuenta y comprobar en Ajustes que es la correcta.
- Cada perfil tiene **«Abrir»** (o **«Traer al frente»** si ya está abierto) y, en **«⋯»**, editar, crear un acceso
  directo, cerrar o quitar de la lista.
- La herramienta vive en la bandeja del sistema: desde allí puedes abrir cualquier perfil.
- Si dos perfiles van a trabajar con Claude Code en la misma carpeta, te avisa antes y te ofrece una copia de trabajo
  de Git para que no se pisen los cambios.
- **Copias** guarda la lista de perfiles y los ajustes de Claude Code (nunca sesiones ni credenciales).
- **Mensajes** (opcional): tus cuentas se pasan información desde los chats de Code. Ver abajo.

## Mensajes entre cuentas

Actívalo en **Mensajes → Activar**. Después, en un chat de Code de una cuenta, pídelo con tus palabras:
«dile a Claude Pro que el error está en login.js y pásale un resumen de lo que hicimos».

1. Ese Claude escribe el mensaje y elige la carpeta: la del proyecto si es lo mismo, una carpeta nueva y vacía
   si es un trabajo aparte, o te pregunta si tiene dudas.
2. Claude Multi-Account abre en la otra cuenta **un chat de Code nuevo** en esa carpeta, con el mensaje ya
   escrito, y te avisa con una notificación. **Lo envías tú** pulsando Enter.
3. En ese chat puedes seguir hablando y pedirle que conteste; la respuesta llega al chat de la primera cuenta
   cuando le escribas.

Cómo funciona: al activarlo, cada perfil recibe una skill (`mensajes-entre-cuentas`) y un hook
`UserPromptSubmit` en el `settings.json` de su configuración de Claude Code (solo se añade y se quita lo de la
herramienta). Los mensajes son archivos en `%USERPROFILE%\.claude-multi-account\buzon`, en tu PC. El chat se
abre con el enlace oficial `claude://code/new` de Claude Desktop, pasado directamente al Claude de esa cuenta.

Límites a propósito: Claude Desktop no permite enviar el mensaje solo, y hacerlo con un script iría contra los
términos de Anthropic (no se permite usar el servicio con bots o scripts); por eso lo envías tú. Se rechazan los
mensajes que parecen llevar contraseñas o claves. Sirve para pasar información, no para que una cuenta ponga a
trabajar a otra sin ti. La herramienta tiene que estar abierta (en la bandeja) para abrir el chat; si no, el
mensaje llega cuando escribas en esa cuenta.

## Qué hace y qué no hace

- ✅ Lanza el `Claude.exe` oficial y busca su ruta en cada lanzamiento (las actualizaciones la cambian).
- ✅ Quita del entorno las variables que anularían tu suscripción (`ANTHROPIC_API_KEY`, …) y avisa solo con sus nombres.
- ✅ Da a cada perfil su propia configuración y carpeta temporal de Claude Code.
- ✅ Avisa cuando Claude Desktop se actualiza y comprueba, sin cambiar nada, que cada perfil sigue encontrando sus datos.
- ❌ No lee, copia ni registra contraseñas, tokens ni cookies. El inicio de sesión lo haces tú en el flujo oficial de Claude.
- ❌ No modifica ni copia archivos de Claude Desktop y no toca el registro del protocolo `claude://`.
- ❌ No necesita administrador, no usa la red y no envía telemetría.
- ❌ No reparte el uso entre cuentas ni las alterna para saltarse límites.

## Limitaciones

- **Separación lógica, no de seguridad:** todos los perfiles corren con tu mismo usuario de Windows. Evita que las
  cuentas se mezclen por accidente, pero no protege una cuenta de un programa malicioso que corra como tú.
- **Se comparten** porque son de Windows o del proyecto: Git, `gh`, SSH, PATH y los archivos del proyecto
  (`CLAUDE.md`, `.claude/`, `.mcp.json`). El panel «Qué comparten» lo explica.
- **Cowork** no puede usarse a la vez en dos perfiles (Claude usa una sola máquina virtual por PC).
- Los datos de los perfiles nuevos, con la versión MSIX, quedan dentro de la carpeta del paquete de Claude
  (`%LOCALAPPDATA%\Packages\Claude_…\LocalCache`): se perderían si desinstalas Claude Desktop.
- **Cerrar la ventana de Claude no lo cierra** (se queda en la bandeja). Cerrar desde la herramienta fuerza el cierre;
  la alternativa sin perder nada es «Salir» en el icono de ese Claude en la bandeja.
- Un perfil abierto **fuera** de la herramienta se ve y se puede traer al frente, pero no cerrar desde aquí.
- Solo Windows y solo la versión MSIX de Claude Desktop.

Es tu responsabilidad usar cada cuenta según los términos de Anthropic: cada cuenta debe ser tuya y no compartirse.

## Para desarrolladores

Hace falta el SDK de .NET 10.

```powershell
dotnet build ClaudeMultiAccount.slnx
dotnet test ClaudeMultiAccount.slnx
powershell -NoProfile -File scripts\publish.ps1   # pruebas + paquete autocontenido en artifacts\
```

La compilación trata cualquier aviso como error.

| Carpeta | Qué es |
|---|---|
| `src/Core` | Núcleo sin interfaz: perfiles, lanzamiento, procesos, copias, instalación, avisos de actualización |
| `src/App` | La ventana `ClaudeMA.exe` (WPF con el tema Fluent de Windows 11) y el icono de la bandeja |
| `src/Cli` | La línea de comandos `cma.exe` |
| `tests/` | Pruebas automáticas (xUnit) |
| `docs/` | Guía de uso y versiones de Claude Desktop probadas |

La línea de comandos sirve para diagnóstico y automatización:

```powershell
cma list                 # perfiles y su estado
cma status               # versión de Claude y procesos de cada perfil
cma launch "Trabajo"     # abre un perfil (el primer inicio de sesión se hace en la ventana)
cma close "Trabajo"      # --forzar si no se cierra solo (se pierde lo no guardado)
cma diagnose             # informe sin secretos ni valores de variables
cma mensaje enviar --de "Personal" --para "Trabajo" --archivo nota.md --carpeta C:\proyectos\web
cma mensaje leer --de "Trabajo"
```

`CMA_HOME` permite usar otra carpeta de datos en lugar de `%LOCALAPPDATA%\ClaudeMultiAccount` (útil para probar).

> Si desarrollas desde una terminal abierta dentro de Claude Desktop (por ejemplo, la pestaña Code), abre la
> herramienta desde el Explorador: Windows desvía a la carpeta privada de Claude lo que crean los programas
> lanzados desde él. Por ejemplo: `Start-Process explorer.exe '"<ruta>\ClaudeMA.exe"'`.

Las reglas de seguridad que sigue el código están en [SECURITY.md](SECURITY.md).

## Licencia

[MIT](LICENSE).
