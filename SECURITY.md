# Seguridad

## Informar de un problema de seguridad

Si encuentras un fallo de seguridad, **no abras un issue público**. Usa «Report a vulnerability» en la pestaña
**Security** de este repositorio (aviso privado). Incluye qué versión usas, qué pasa y cómo reproducirlo, sin
adjuntar credenciales, tokens ni datos de tus cuentas.

## 1. Alcance

**Se protege:** (1) las sesiones de cada cuenta de Anthropic; (2) el código y los archivos de tus proyectos; (3) las
transcripciones y el historial de Claude Code (son texto plano en disco); (4) la integridad de tu instalación de
Claude Desktop; (5) tu cuenta, evitando prácticas contrarias a los términos de Anthropic.

**Fuera de alcance:** proteger frente a programas maliciosos que corran con tu mismo usuario de Windows o frente a un
administrador del equipo. Windows cifra las sesiones por usuario, no por carpeta: cualquier proceso que corra como tú
puede leer **todos** los perfiles. El aislamiento de esta herramienta es **lógico (evita mezclas accidentales), no una
frontera de seguridad**. Si necesitas una frontera real, usa otro usuario de Windows o una máquina virtual.

## 2. Fronteras de confianza

```
[Tú] → [Claude Multi-Account] → [Claude.exe oficial] → [Servidores de Anthropic]
          │ (solo lanza y vigila)     │ (guarda la sesión, no la herramienta)
          └─ NO cruza: tokens, cookies, credenciales
```

## 3. Modelo de amenazas

| ID | Amenaza | Vector | Mitigación |
|---|---|---|---|
| T1 | **Cuentas cruzadas en el inicio de sesión** | El enlace de vuelta `claude://` es global en Windows | Inicio de sesión guiado de **un perfil a la vez**, con los demás cerrados; el usuario comprueba el correo; no se toca el registro |
| T2 | **Ajustes o memoria de Code compartidos** | `--user-data-dir` no separa Claude Code | `CLAUDE_CONFIG_DIR` y `CLAUDE_CODE_TMPDIR` por perfil |
| T3 | **Variables de entorno que anulan la suscripción** | `ANTHROPIC_API_KEY` y similares | `EnvScrubber` las quita del entorno del perfil y avisa solo con sus nombres |
| T4 | **Recursos compartidos de Windows** | Credenciales de Git, `gh`, SSH, PATH | Panel «Qué comparten»; no se automatizan credenciales |
| T5 | **Configuración de proyecto compartida** | `CLAUDE.md`, `.claude/`, `.mcp.json` en el repositorio | Aviso en la interfaz antes de abrir |
| T6 | **Dos agentes sobre los mismos archivos** | Misma carpeta de trabajo | `FolderGuard` avisa y ofrece una copia de trabajo de Git |
| T7 | **Lectura de secretos por la propia herramienta** | Un fallo o un diseño que lea credenciales | **Regla dura:** el código nunca abre `.credentials.json`, `config.json`, `Cookies`, `Local Storage` ni `.claude.json`; una prueba automática falla si aparecen esas rutas |
| T8 | **Registros con secretos** | Línea de comandos o entorno en el registro | Nunca se registran valores de entorno ni la línea de comandos completa |
| T9 | **Copias de seguridad con secretos** | Copiar carpetas enteras | Lista cerrada de archivos; se omite cualquier archivo con algo parecido a una clave |
| T10 | **Pérdida de datos** | Mover o borrar carpetas | Nunca se mueven ni borran datos de Claude; desinstalar conserva por defecto |
| T11 | **Código de terceros** | Ejecutables sin firmar, `irm \| iex` | No se ejecuta código de terceros; no se pide desactivar protecciones de Windows |
| T12 | **Cuenta sancionada por los términos** | Rotación de cuentas, proxies, intermediar tokens | §6 |
| T13 | **Elevación de privilegios** | Pedir administrador sin necesidad | No requiere administrador |
| T14 | **Actualización de Claude que rompe el aislamiento** | Cambios de comportamiento | Aviso de actualización, comprobación de compatibilidad y lista de versiones probadas; una versión no probada no se bloquea, solo se avisa |
| T15 | **Inyección por rutas o argumentos** | Nombres de perfil con caracteres especiales | Validación; argumentos pasados como lista; nombres de carpeta con caracteres permitidos |

## 4. Credenciales

1. La herramienta **no pide, no ve y no guarda** contraseñas, tokens, cookies ni códigos de inicio de sesión. Te
   identificas solo en el flujo oficial de Claude.
2. No hay medidor de uso que consulte `claude.ai` con tu sesión.
3. No existe ningún campo de contraseña. El «correo de referencia» es un texto libre que escribes tú.
4. No se modifican `.credentials.json`, `config.json` ni `.claude.json`.

## 5. Permisos

| Acción | Permiso |
|---|---|
| Crear carpetas bajo `%APPDATA%`, `%LOCALAPPDATA%` y `%USERPROFILE%`; crear accesos directos; lanzar `Claude.exe` | Usuario estándar |
| Cerrar procesos, solo los del perfil que se cierra | Usuario estándar |
| Instalarse en `%LOCALAPPDATA%\Programs` y registrar su entrada en `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeMultiAccount` | Usuario estándar |
| **Nunca:** escribir en HKLM, instalar servicios, desactivar Defender o Smart App Control, tocar `WindowsApps`, escribir en `HKCU\Software\Classes\claude` | — |

## 6. Términos de uso (principios; no es asesoría legal)

1. Cada cuenta es de una persona y se usa para su propósito. No se comparten inicios de sesión.
2. La herramienta **no** alterna cuentas según el uso restante, no reparte carga y no reenvía peticiones.
3. No intermedia credenciales ni modifica el programa oficial.
4. Si tu cuenta de trabajo es de Team o Enterprise, tu organización puede aplicar sus propias políticas. No mezcles
   datos de la empresa con la cuenta personal.

## 7. Controles contra la mezcla de cuentas

| Control | Dónde |
|---|---|
| `--user-data-dir` distinto por perfil | `LaunchPlanner` |
| `CLAUDE_CONFIG_DIR` y `CLAUDE_CODE_TMPDIR` distintos por perfil | `LaunchPlanner` |
| Entorno sin `ANTHROPIC_*` ni `CLAUDE_CODE_USE_*` | `EnvScrubber` |
| Aviso de carpeta de trabajo compartida | `FolderGuard` |
| Inicio de sesión de un perfil a la vez, con comprobación del correo | Asistente de inicio de sesión |
| Copias sin secretos | `BackupService` |

## 8. Privacidad

- Las transcripciones y el historial de Claude Code son **texto plano** en la carpeta de configuración de cada perfil.
- La herramienta **no usa la red** y no envía telemetría (hay una prueba automática que lo comprueba).
- Sus propios datos (lista de perfiles, estado, copias y registros) están en `%LOCALAPPDATA%\ClaudeMultiAccount`.

## 9. Requisitos de seguridad

| Requisito | Cómo se cumple |
|---|---|
| No guardar contraseñas | No se guardan (§4) |
| No registrar tokens, cookies ni credenciales | T7, T8 |
| No desactivar protecciones de Windows | §5, T11 |
| Sin administrador | §5 |
| No modificar programas de Anthropic | §6 |
| No borrar datos sin consentimiento | T10 |
| No eludir la autenticación ni los límites | §6 |
| Explicar qué comparte cada perfil | Panel «Qué comparten» |
