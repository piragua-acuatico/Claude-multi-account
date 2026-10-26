using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace ClaudeMultiAccount.App.Services;

/// <summary>Lo que un segundo arranque le pide a la herramienta que ya está abierta.</summary>
public sealed record InstanceMessage(string? LaunchProfile)
{
    private const string ShowCommand = "show";
    private const string LaunchCommand = "launch ";
    private const int MaxLength = 256;

    public static InstanceMessage From(StartupOptions options) => new(options.LaunchProfile);

    public string Serialize() => LaunchProfile is null ? ShowCommand : LaunchCommand + LaunchProfile;

    /// <summary>Null si el texto no es un mensaje válido (se ignora).</summary>
    public static InstanceMessage? Parse(string? line)
    {
        if (line is null || line.Length > MaxLength || line.Any(char.IsControl))
        {
            return null;
        }
        if (line == ShowCommand)
        {
            return new InstanceMessage(LaunchProfile: null);
        }
        if (line.StartsWith(LaunchCommand, StringComparison.Ordinal))
        {
            var profile = line[LaunchCommand.Length..].Trim();
            return profile.Length == 0 ? null : new InstanceMessage(profile);
        }
        return null;
    }
}

/// <summary>
/// Una sola herramienta abierta por usuario y sesión: con la herramienta en la bandeja, un acceso
/// directo no debe abrir otra copia, sino pedirle a la abierta que lance el perfil. El mutex dice si ya hay
/// una; la tubería con nombre lleva el encargo y solo admite conexiones del mismo usuario (CurrentUserOnly).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string BaseName = "ClaudeMultiAccount";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _pipeName;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Nombre por sesión de Windows (otra sesión u otro usuario tienen su propia herramienta).</summary>
    public static string DefaultName => $"{BaseName}-{Process.GetCurrentProcess().SessionId}-{Environment.UserName}";

    /// <summary>La instancia principal, o null si ya había otra abierta.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, name);
    }

    /// <summary>Envía el encargo a la instancia abierta. False si no respondió a tiempo.</summary>
    public static bool TrySend(string name, InstanceMessage message)
    {
        // La instancia abierta tiene que poder traer su ventana al frente al recibir el encargo.
        _ = AllowSetForegroundWindow(AsfwAny);
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)ConnectTimeout.TotalMilliseconds);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(message.Serialize());
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Atiende encargos hasta que se libere el objeto. <paramref name="onMessage"/> se llama fuera del hilo de la interfaz.</summary>
    public void Listen(Action<InstanceMessage> onMessage) => _ = ListenAsync(onMessage, _stop.Token);

    private async Task ListenAsync(Action<InstanceMessage> onMessage, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop);
                using var reader = new StreamReader(server, new UTF8Encoding(false));
                var line = await ReadLimitedLineAsync(reader, stop);
                if (InstanceMessage.Parse(line) is { } message)
                {
                    onMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // Un cliente que se va a mitad, o el canal ocupado un momento, no debe parar la escucha;
                // la pausa evita dar vueltas sin parar si el error se repite.
                try
                {
                    await Task.Delay(500, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Lee una línea sin aceptar más de unos cientos de caracteres.</summary>
    private static async Task<string?> ReadLimitedLineAsync(StreamReader reader, CancellationToken stop)
    {
        var buffer = new char[512];
        var builder = new StringBuilder();
        while (builder.Length < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length - builder.Length), stop);
            if (read == 0)
            {
                break;
            }
            builder.Append(buffer, 0, read);
            var newline = builder.ToString().IndexOf('\n');
            if (newline >= 0)
            {
                return builder.ToString(0, newline).TrimEnd('\r');
            }
        }
        return builder.Length == 0 ? null : builder.ToString().TrimEnd('\r', '\n');
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Solo puede soltarlo el hilo que lo tomó; al terminar el proceso Windows lo suelta igual.
        }
        _mutex.Dispose();
    }

    private const int AsfwAny = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
