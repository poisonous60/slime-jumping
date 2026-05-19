using UnityEditor;
using UnityEngine;

namespace SlimeJumping.EditorTools.UI
{
    /// Single menu item that rebuilds every UI prefab in dependency order, so a
    /// juice tuning pass doesn't require clicking through four separate items.
    public static class UIRebuildAll
    {
        [MenuItem("Tools/SlimeJumping/UI/Rebuild ALL UI Prefabs", priority = 0)]
        public static void RebuildAll()
        {
            // Force the default juice profile to exist before the panel builders
            // try to wire it.
            UISetupCommon.LoadOrCreateDefaultJuiceProfile();

            UIRootSetup.Build();
            MainMenuPanelSetup.Build();
            OptionsPanelSetup.Build();
            EndPanelSetup.Build();
            GramophonePanelSetup.Build();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UIRebuildAll] All UI prefabs rebuilt.");
        }
    }
}
