namespace SlimeJumping.Rendering
{
    /// <summary>VFX categories for VFXColorPalette / ParticlePaletteBinder.
    /// Append-only: binders serialize this as int, so reorder/delete silently corrupts scene refs.</summary>
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
