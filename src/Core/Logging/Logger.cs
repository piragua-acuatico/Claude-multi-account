using System.Text.RegularExpressions;

namespace ClaudeMultiAccount.Core.Logging;

/// <summary>
/// Redacta antes de escribir. Regla: nunca se pasan al registro valores de variables de entorno ni la
/// línea de comandos completa; esto es una segunda red por si algo se cuela.
/// </summary>
public static partial class Redactor
{
    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]+")]
    private static partial Regex AnthropicKey();

    // Más largo que un GUID (36) para no ocultar identificadores de perfil.
    [GeneratedRegex(@"[A-Za-z0-9_\-\.]{40,}")]
    private static partial Regex LongToken();

    public static string Redact(string text, string? userProfile)
    {
        var result = AnthropicKey().Replace(text, "[REDACTADO]");
        result = LongToken().Replace(result, "[REDACTADO]");
        if (!string.IsNullOrEmpty(userProfile))
        {
            result = result.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }
}

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>Registro diario en logs\cma-AAAAMMDD.log. La carpeta se crea al escribir la primera línea. Nunca lanza.</summary>
public sealed class FileLogger(string logsDir) : ILogger
{
    private static readonly object Gate = new();
    private readonly string? _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("AVISO", message);

    public void Error(string message, Exception? exception = null) =>
        // Del error solo se guarda el tipo y el mensaje, redactados.
        Write("ERROR", exception is null ? message : $"{message} ({exception.GetType().Name}: {exception.Message})");

    private void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {Redactor.Redact(message, _userProfile)}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(logsDir);
                File.AppendAllText(Path.Combine(logsDir, $"cma-{DateTime.Now:yyyyMMdd}.log"), line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // El registro nunca debe impedir abrir o cerrar un perfil.
        }
    }
}

public sealed class NullLogger : ILogger
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
