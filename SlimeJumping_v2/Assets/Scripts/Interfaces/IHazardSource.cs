namespace SlimeJumping.Interfaces
{
    public interface IHazardSource
    {
        HazardEffect GetHazardEffect();
        bool IsActive { get; }
    }
}
