using System.Collections;

namespace ClaudeMultiAccount.Core.Abstractions;

/// <summary>Acceso al entorno del proceso (variables). Interfaz para poder simularlo en pruebas.</summary>
public interface IEnvironment
{
    string? Get(string name);

    /// <summary>Copia de todas las variables del proceso actual (comparación sin distinguir mayúsculas).</summary>
    IReadOnlyDictionary<string, string> GetAll();

    /// <summary>
    /// Entorno por defecto del usuario de Windows, el mismo que recibe un programa abierto desde el menú Inicio.
    /// No incluye lo que haya añadido la consola desde la que se ejecuta la herramienta (p. ej. una sesión
    /// de Claude Code). Null si Windows no lo pudo construir.
    /// </summary>
    IReadOnlyDictionary<string, string>? GetDefaultUserEnvironment();
}

public sealed class SystemEnvironment : IEnvironment
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);

    public IReadOnlyDictionary<string, string>? GetDefaultUserEnvironment() =>
        Native.UserEnvironment.TryCreateDefault();

    public IReadOnlyDictionary<string, string> GetAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                result[key] = value;
            }
        }
        return result;
    }
}
