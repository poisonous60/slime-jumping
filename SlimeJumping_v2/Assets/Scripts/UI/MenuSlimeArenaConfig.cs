using UnityEngine;

namespace SlimeJumping.UI
{
    /// Shared menu slime arena tuning; canonical asset
    /// <c>Assets/Resources/MenuSlimeArenaConfig.asset</c> (auto-loaded).
    [CreateAssetMenu(fileName = "MenuSlimeArenaConfig",
        menuName = "SlimeJumping/Menu Slime Arena Config", order = 0)]
    public sealed class MenuSlimeArenaConfig : ScriptableObject
    {
        [Tooltip("Thickness of the invisible floor and side walls (world units).")]
        [Min(0.1f)] public float wallThickness = 1f;

        const string ResourcesPath = "MenuSlimeArenaConfig";

        /// Override if assigned, else the Resources asset, else null.
        public static MenuSlimeArenaConfig Resolve(MenuSlimeArenaConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<MenuSlimeArenaConfig>(ResourcesPath);
        }
    }
}
