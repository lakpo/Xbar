using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Xbar.Core;

namespace Xbar.Update;

/// <summary>
/// Remplacement de Xbar.exe par une version plus recente.
///
/// Windows interdit d'ecrire dans un executable en cours d'execution, mais pas de le renommer.
/// La nouvelle version est donc telechargee a cote (Xbar.exe.download), verifiee, puis
/// l'executable courant est renomme en Xbar.exe.old et le nouveau prend sa place. Xbar se
/// relance ensuite avec --updated : la nouvelle instance attend que l'ancienne ait quitte,
/// puis supprime Xbar.exe.old.
///
/// Rien n'est ecrit ailleurs que dans le dossier de l'executable : la configuration (dans
/// %AppData%\Xbar ou Data\ en portable) n'est pas touchee.
/// </summary>
public static class Updater
{
    /// <summary>Argument de la relance qui suit une mise a jour.</summary>
    public const string UpdatedSwitch = "--updated";

    private const string DownloadSuffix = ".download";
    private const string OldSuffix = ".old";

    private static string? ExePath => Environment.ProcessPath;

    /// <summary>
    /// Raison pour laquelle cette copie de Xbar ne peut pas se mettre a jour seule, ou null si
    /// elle le peut. Dans ce cas, Xbar se contente de signaler la nouvelle version.
    /// </summary>
    public static string? CannotInstallReason()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return "chemin de l'executable inconnu";

        var dir = Path.GetDirectoryName(exe)!;

        // Une compilation de developpement (dotnet run, Visual Studio) laisse Xbar.dll a cote de
        // l'executable : la remplacer par la version publiee n'aurait aucun sens.
        if (File.Exists(Path.Combine(dir, "Xbar.dll"))) return "version de developpement";

        try
        {
            var probe = Path.Combine(dir, $".xbar-write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception)
        {
            return "dossier de l'executable en lecture seule";
        }

        return null;
    }

    /// <summary>
    /// Telecharge et met en place la nouvelle version. Renvoie true si Xbar.exe a ete
    /// remplace : il reste alors a relancer Xbar (voir Restart).
    /// </summary>
    public static async Task<bool> InstallAsync(UpdateChecker.UpdateInfo update, CancellationToken token = default)
    {
        var asset = update.Executable;
        var exe = ExePath;
        if (asset is null || string.IsNullOrEmpty(exe)) return false;

        var download = exe + DownloadSuffix;
        var old = exe + OldSuffix;

        try
        {
            Log.Info($"Telechargement de la version {update.Version} : {asset.DownloadUrl}");
            await DownloadAsync(asset, download, token).ConfigureAwait(false);

            // Une ancienne copie peut subsister si sa suppression a echoue au lancement precedent.
            if (File.Exists(old))
            {
                try { File.Delete(old); }
                catch (Exception) { old = $"{exe}.{DateTime.Now:yyyyMMddHHmmss}{OldSuffix}"; }
            }

            File.Move(exe, old);
            try
            {
                File.Move(download, exe);
            }
            catch
            {
                // Retour a l'etat initial : l'executable doit toujours etre la.
                File.Move(old, exe);
                throw;
            }

            Log.Info($"Version {update.Version} installee, relance de Xbar");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Installation de la version {update.Version}", ex);
            TryDelete(download);
            return false;
        }
    }

    private static async Task DownloadAsync(GitHubAsset asset, string destination, CancellationToken token)
    {
        using var client = UpdateChecker.CreateClient(TimeSpan.FromMinutes(10));
        using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        var header = new byte[2];

        await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        await using (var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (length < 2) Array.Copy(buffer, 0, header, (int)length, Math.Min(read, 2 - (int)length));
                sha.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                length += read;
            }
        }

        // Verifications avant de toucher a l'executable en place : un fichier tronque ou altere
        // rendrait Xbar inutilisable.
        if (asset.Size > 0 && length != asset.Size)
            throw new InvalidDataException($"taille inattendue ({length} octets au lieu de {asset.Size})");

        if (header[0] != 'M' || header[1] != 'Z')
            throw new InvalidDataException("le fichier telecharge n'est pas un executable Windows");

        if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var expected = digest["sha256:".Length..];
            var actual = Convert.ToHexString(sha.GetHashAndReset());
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("empreinte SHA-256 differente de celle publiee par GitHub");
        }
        else Log.Warn("Pas d'empreinte publiee pour cet executable : seules sa taille et son en-tete sont verifies");
    }

    /// <summary>Lance la nouvelle version ; l'appelant doit ensuite fermer cette instance.</summary>
    public static bool Restart(string version)
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                ArgumentList = { UpdatedSwitch, version }
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Relance apres mise a jour", ex);
            return false;
        }
    }

    /// <summary>
    /// Supprime les restes d'une mise a jour (Xbar.exe.old, telechargement interrompu).
    /// L'ancienne instance peut mettre un instant a liberer son fichier : on reessaie.
    /// </summary>
    public static async Task CleanupAsync()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return;

        var dir = Path.GetDirectoryName(exe)!;
        var name = Path.GetFileName(exe);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var leftovers = Directory.EnumerateFiles(dir, name + ".*")
                .Where(f => f.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(DownloadSuffix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (leftovers.Count == 0) return;

            foreach (var file in leftovers)
                if (TryDelete(file)) Log.Info($"Ancienne version supprimee : {Path.GetFileName(file)}");

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"Suppression de {Path.GetFileName(path)} : {ex.Message}");
            return false;
        }
    }
}
