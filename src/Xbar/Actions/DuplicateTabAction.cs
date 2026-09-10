using Xbar.ExplorerIntegration;
using Xbar.Localization;

namespace Xbar.Actions;

/// <summary>
/// Ouvre le dossier affiche dans un nouvel onglet de la meme fenetre.
///
/// Windows n'expose pas de duplication d'onglet : on reutilise la creation d'onglet de
/// ExplorerNavigator, puis on y navigue vers le dossier courant. Un onglet pose sur un dossier
/// virtuel (Accueil, Ce PC, Reseau) n'a pas de chemin exploitable et n'est donc pas duplicable.
/// </summary>
public sealed class DuplicateTabAction : XbarAction
{
    public const string ActionId = "tabs.duplicate";

    public override string Id => ActionId;
    public override string Title => Strings.Get("Action_DuplicateTab");
    public override string Glyph => "\uE8C8"; // Segoe Fluent Icons : Copy

    public override async Task ExecuteAsync(ActionContext context)
    {
        var folder = context.CurrentFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            context.Report(Strings.Get("Msg_DuplicateTabNoFolder"), false);
            return;
        }

        if (await ExplorerNavigator.OpenInNewTabAsync(context.ExplorerWindow, folder))
            context.Report(Strings.Get("Msg_DuplicateTabDone"));
        else
            context.Report(Strings.Get("Msg_DuplicateTabFailed"), false);
    }
}
