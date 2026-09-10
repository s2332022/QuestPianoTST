namespace QuestPianoMotion.Research
{
    /// <summary>Receives immutable hand samples for logging or persistence.</summary>
    public interface IHandTrackingSampleSink
    {
        /// <summary>Consumes one sample. Implementations should avoid blocking the XR update callback.</summary>
        void Write(in HandTrackingSample sample);
    }
}
