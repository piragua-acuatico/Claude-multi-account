# Claude Multi-Account — Guía de uso

Claude Multi-Account abre el **Claude Desktop oficial** una vez por cada cuenta tuya, cada una con su propia
sesión, sus chats de Code y sus ajustes. No es un Claude nuevo ni una copia: es el mismo programa de Anthropic,
abierto con carpetas distintas. **No es una herramienta de Anthropic.**

## Qué necesitas

- Windows 11 con **Claude Desktop instalado desde la Tienda de Microsoft** (es lo que está probado).
- Nada más: la versión empaquetada lleva dentro todo lo que necesita.

## Instalar

1. Descomprime `ClaudeMultiAccount-<versión>-win-x64.zip`: clic derecho → «Extraer todo…» → «Extraer». No lo abras
   desde dentro del zip: el programa necesita sus demás archivos al lado.
2. Abre `ClaudeMA.exe`. Así ya funciona como versión portátil.
3. Para instalarla, pulsa **«Instalar en este equipo…»**. Se instala solo para tu usuario, **sin permisos de
   administrador**, en `%LOCALAPPDATA%\Programs\Claude Multi-Account`, con su entrada en el menú Inicio y en
   «Aplicaciones instaladas». Después puedes borrar la carpeta del zip.

Windows puede avisar de que el programa no está firmado (SmartScreen): elige «Más información» → «Ejecutar de todas
formas» solo si descargaste el zip de un sitio en el que confías. **No hace falta desactivar ninguna protección**
(ni Defender ni Smart App Control).

**Comprobar que el zip es el original.** Junto al zip va un archivo `.sha256` con su huella. En PowerShell:

```powershell
Get-FileHash .\ClaudeMultiAccount-0.1.0-win-x64.zip -Algorithm SHA256
```

El valor que sale debe ser **idéntico** al del archivo `.sha256`. Si no coincide, no lo abras: el zip no es el que se
generó.

## Primeros pasos

- La primera vez que abres la herramienta, tu lista empieza con el **Claude principal**: tu Claude de siempre, con
  sus datos de siempre. La herramienta no lo cambia (puedes renombrarlo, p. ej. «Claude Max», con «⋯ → Editar perfil…»).
- **«Nuevo perfil»** crea otro Claude para otra cuenta. Sus carpetas se llaman `Multi-Claude-<Nombre>` y
  `.multi-claude-<nombre>`, y Claude las crea la primera vez que lo abres.
- La **primera vez** que abres un perfil nuevo sale el **asistente de inicio de sesión**:
  1. cierra los demás Claude (si no, el inicio de sesión podría ir a otra ventana);
  2. abre solo ese perfil e inicia sesión en tu navegador, **mirando qué correo aparece antes de aceptar**;
  3. comprueba en Ajustes de ese Claude que el correo es el de esa cuenta.

  La herramienta nunca ve tu contraseña ni tu sesión.

## Uso diario

- Cada perfil es una tarjeta con su estado (verde: abierto; gris: cerrado; azul: abriéndose o cerrándose; rojo:
  error). **«Abrir»** lo abre; si ya está abierto, el botón dice **«Traer al frente»**. El resto de acciones está en
  el botón **«⋯»** de la tarjeta.
- Cerrar la ventana de Claude con la X **no lo cierra**: se queda en la bandeja del sistema. **«⋯ → Cerrar…»** fuerza
  el cierre (se pierde lo que no esté guardado); la alternativa es «Salir» en el icono de ese Claude en la bandeja.
- La herramienta sigue el **modo claro u oscuro** de Windows.
- **Bandeja:** la herramienta vive junto al reloj. Clic derecho para abrir o cerrar perfiles; la X de su ventana
  solo la esconde; «Salir» la cierra (sin cerrar ningún Claude).
- **Accesos directos:** «⋯ → Crear acceso directo…» crea un icono en el escritorio o en el menú Inicio que abre ese
  perfil aunque la herramienta esté cerrada.
- **«⋯ → Editar perfil…» / «⋯ → Quitar de la lista…»:** cambiar nombre, color, inicial, correo de referencia y
  carpetas de trabajo. Quitar un perfil solo lo saca de la lista: sus carpetas y su sesión se quedan en tu PC.

## Chats de Code

Los chats normales (pestaña Chat) están en tu cuenta y se ven en cualquier sitio. Los **chats de Code** se guardan en
tu PC, en las carpetas del Claude donde los creaste: un chat de Code creado en el Claude principal con la cuenta Pro
**no aparece** en el perfil Pro, y al revés. Usa siempre el mismo perfil para cada cuenta.

## Carpetas de trabajo y Git

Si abres un perfil en una carpeta que ya usa otro perfil abierto, la herramienta avisa: dos Claude en la misma
carpeta pueden pisarse los cambios. Puede crear una **copia de trabajo de Git** (otra carpeta y otra rama,
`multi/<perfil>`) para el segundo perfil; luego juntas los cambios con un merge o un PR. También avisa de los
`CLAUDE.md`, `.claude\` y `.mcp.json` del proyecto, que valen para **todas** las cuentas.

Git, GitHub, SSH y tus variables de usuario son de tu usuario de Windows y los comparten todos los perfiles: si una
cuenta es de trabajo, configura el correo en ese repositorio (`git config user.email`).

## Actualizaciones de Claude

Cuando Claude Desktop se actualiza, la herramienta avisa. Pulsa **«Comprobar compatibilidad»**: comprueba, sin cambiar
nada, que cada perfil sigue encontrando sus carpetas. Si algo falla, no abras ese perfil hasta revisarlo.

## Copias de seguridad

«Copias» guarda la lista de perfiles y, de cada perfil, los `settings.json` y `CLAUDE.md` de Claude
Code. **Nunca** copia sesiones, credenciales, chats ni historiales, y omite cualquier archivo que parezca contener una
clave. Restaurar muestra antes qué cambia y guarda una copia de lo que reemplaza.

## Desinstalar

En **Configuración → Aplicaciones → Aplicaciones instaladas → Claude Multi-Account → Desinstalar**. Antes sal de la
herramienta («Salir» en su icono). Se quitan la herramienta, su entrada y los accesos directos que creó. Por defecto
**se conservan** tu lista de perfiles y tus copias; **nunca** se tocan Claude Desktop ni las carpetas de tus perfiles
(la ventana de desinstalar te dice dónde están, por si quieres borrarlas tú).

## Limitaciones que conviene saber

- **No es una barrera de seguridad:** cualquier programa que se ejecute con tu usuario de Windows puede leer las
  carpetas de todos los perfiles. Sirve para no mezclar cuentas por accidente.
- **El enlace `claude://` es uno solo** para todo Windows: por eso el primer inicio de sesión se hace con los demás
  Claude cerrados.
- La herramienta compara las **carpetas de trabajo que tú pones** en cada perfil; no sabe qué carpeta elige cada chat
  de Code dentro de Claude.
- Solo está probada con Claude Desktop de la Tienda en Windows 11; cada versión nueva de Claude puede cambiar cosas.
- **Cowork** solo puede usarse en un perfil a la vez: Claude usa una única máquina virtual por PC.
- **Computer use** y otros ajustes de Claude son de cada perfil: si los quieres en un perfil nuevo, actívalos en sus
  Ajustes.
- Los datos de los perfiles nuevos se guardan dentro de la carpeta de Claude Desktop de la Tienda: **si desinstalas
  Claude Desktop, se pierden** (las sesiones se vuelven a iniciar y los chats normales siguen en tu cuenta).
- **Ábrela desde el menú Inicio, el escritorio o el Explorador, no desde una terminal de Claude Code dentro de Claude
  Desktop.** Claude Desktop es una app de la Tienda y Windows puede desviar a su carpeta privada lo que crean los
  programas que se abren desde él: la herramienta guardaría ahí sus perfiles (y su entrada de «Aplicaciones
  instaladas») y, abierta luego normalmente, no vería tus perfiles. Tus datos no se pierden; están en
  `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\ClaudeMultiAccount`.

## Si algo va mal

| Qué pasa | Qué hacer |
|---|---|
| «No se encontró Claude Desktop» | Instala Claude Desktop desde la Tienda de Microsoft. |
| «Claude Multi-Account ya está abierto, pero no responde» | «Salir» en su icono de la bandeja o ciérralo en el Administrador de tareas. |
| Un perfil muestra la cuenta equivocada | Ciérralo, cierra sesión dentro de ese Claude y vuelve a iniciar sesión con los demás Claude cerrados. |
| Quieres saber qué pasó | Los registros están en `%LOCALAPPDATA%\ClaudeMultiAccount\logs` (sin contraseñas ni valores de variables). |
