namespace SlimeJumping.Rendering
{
    /// <summary>
    /// Element-based VFX categories. Used by VFXColorPalette and ParticlePaletteBinder.
    /// RULE: Append-only. Never delete or rename existing values — Binder components
    /// serialize enum as int, so reordering would silently corrupt all scene references.
    /// </summary>
    public enum VFXType
    {
        Wind,
        Fire,
        Water,
        Dust,
        Lightning,
        Pipe,
        Slice
    }
}
