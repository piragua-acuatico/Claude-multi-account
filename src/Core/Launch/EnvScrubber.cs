namespace ClaudeMultiAccount.Core.Launch;

public sealed record ScrubResult(Dictionary<string, string> Environment, IReadOnlyList<string> RemovedOverrides);

/// <summary>
/// Limpia el entorno que hereda el proceso hijo (no toca el del sistema). Devuelve solo NOMBRES, nunca valores.
/// </summary>
public static class EnvScrubber
{
    /// <summary>Variables que anulan la suscripción o la cuenta. Se avisa si estaban.</summary>
    public static readonly IReadOnlyList<string> OverrideVariables =
    [
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_AUTH_TOKEN",
        "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDE_CODE_USE_BEDROCK",
        "CLAUDE_CODE_USE_VERTEX",
        "CLAUDE_CODE_USE_FOUNDRY",
        "ANTHROPIC_PROFILE",
        "ANTHROPIC_BASE_URL",
    ];

    /// <summary>
    /// Variables que fija cada perfil. Se quitan siempre de lo heredado: así el perfil principal nunca recibe
    /// el CLAUDE_CONFIG_DIR de otro (p. ej. si la herramienta se abrió desde una consola que lo tenía).
    /// </summary>
    public static readonly IReadOnlyList<string> ProfileVariables =
    [
        "CLAUDE_CONFIG_DIR",
        "CLAUDE_CODE_TMPDIR",
    ];

    public static ScrubResult Scrub(IReadOnlyDictionary<string, string> inherited)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();

        foreach (var (name, value) in inherited)
        {
            if (Contains(OverrideVariables, name))
            {
                removed.Add(name.ToUpperInvariant());
                continue;
            }
            if (Contains(ProfileVariables, name))
            {
                continue;
            }
            environment[name] = value;
        }

        removed.Sort(StringComparer.Ordinal);
        return new ScrubResult(environment, removed);
    }

    private static bool Contains(IReadOnlyList<string> list, string name) =>
        list.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
}
