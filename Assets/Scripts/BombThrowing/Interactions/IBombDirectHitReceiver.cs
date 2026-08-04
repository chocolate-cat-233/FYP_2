namespace SuperFinalFuse.BombThrowing.Interactions
{
    public interface IBombDirectHitReceiver
    {
        BombHitResult ReceiveBombHit(BombDirectHit hit);
    }
}
