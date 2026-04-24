namespace SlimeJumping.UI.Framework
{
    /// Which Canvas inside a UIRoot a panel is reparented under.
    /// Sorting order is baked into the UIRoot prefab per layer.
    public enum UILayer
    {
        Hud,
        Menu,
        Modal,
        Fullscreen,
    }
}
