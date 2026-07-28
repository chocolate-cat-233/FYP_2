namespace SuperFinalFuse.Explosion
{
    /// <summary>
    /// Implemented by objects that react to the expanding shockwave.
    /// </summary>
    public interface IShockwaveReactive
    {
        void OnShockwaveHit(ShockwaveHit hit);
    }
}
