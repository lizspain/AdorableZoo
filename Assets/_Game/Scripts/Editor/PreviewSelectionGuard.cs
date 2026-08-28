using UnityEditor;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Deselects any Hierarchy object that's about to be torn down by exiting Play Mode, if it's
    /// one of the runtime-only preview objects OfferTableauController/ToyAttachmentPreviewWindow
    /// create (named "[TableauPreview...]" / "[Preview] ..."). The per-object Selection-null
    /// guards already in each ClearPreview() only cover destroys that happen *during* Play --
    /// they can't run for the mass teardown Unity performs when Play Mode itself exits, so a
    /// preview object still selected in the Hierarchy at that moment races the Inspector's next
    /// repaint against the teardown and throws MissingReferenceException /
    /// SerializedObjectNotCreatableException. Harmless Console noise, but this closes the gap.
    /// </summary>
    [InitializeOnLoad]
    internal static class PreviewSelectionGuard
    {
        static PreviewSelectionGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;

            var selected = Selection.activeGameObject;
            if (selected == null) return;

            if (selected.name.StartsWith("[TableauPreview") || selected.name.StartsWith("[Preview]"))
            {
                Selection.activeGameObject = null;
            }
        }
    }
}
