namespace ClaudeMultiAccount.App.ViewModels;

/// <summary>Una opción del menú del icono de la bandeja. Sin acción, es solo texto.</summary>
public sealed record TrayMenuEntry(string Text, bool Enabled, Func<Task>? Action);
