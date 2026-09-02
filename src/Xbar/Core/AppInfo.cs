using System.Reflection;

namespace Xbar.Core;

public static class AppInfo
{
    public const string Name = "Xbar";

    /// <summary>Dépôt GitHub utilisé pour la vérification des mises à jour.</summary>
    public const string GitHubRepository = "lakpo/Xbar";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
