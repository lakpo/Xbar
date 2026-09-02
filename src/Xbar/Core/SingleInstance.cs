using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Xbar.Core;

/// <summary>
/// Une seule instance de Xbar. Les instances suivantes envoient une commande à
/// l'instance en place (ouvrir les paramètres) puis s'arrêtent.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Xbar.SingleInstance";
    private const string PipeName = "Xbar.Ipc";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _cts;

    public bool IsFirstInstance { get; }

    /// <summary>Commande reçue d'une autre instance (thread d'arrière-plan).</summary>
    public event Action<string>? CommandReceived;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsFirstInstance = created;
    }

    public void StartServer()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServerLoopAsync(_cts.Token));
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var command = (await reader.ReadToEndAsync(token).ConfigureAwait(false)).Trim();
                if (command.Length > 0) CommandReceived?.Invoke(command);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Warn($"Canal IPC : {ex.Message}");
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Envoie une commande à l'instance déjà en cours.</summary>
    public static bool SendCommand(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(command);
            writer.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Impossible de joindre l'instance en cours : {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { if (IsFirstInstance) _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
