using UnityEditor;
using UnityEngine;

namespace SlimeJumping.EditorTools.UI
{
    /// Rebuilds every UI prefab in dependency order from one menu item.
    public static class UIRebuildAll
    {
        [MenuItem("Tools/SlimeJumping/UI/Rebuild ALL UI Prefabs", priority = 0)]
        public static void RebuildAll()
        {
            // Default juice profile must exist before the panel builders wire it.
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
