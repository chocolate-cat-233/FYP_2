namespace SuperFinalFuse.PlayerLife
{
    public interface ICheckpointStateParticipant
    {
        void CaptureCheckpointState();
        void RestoreCheckpointState();
    }
}
