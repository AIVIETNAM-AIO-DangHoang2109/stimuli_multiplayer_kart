namespace Extra.ScreenRecording
{
    public struct FrameData
    {
        public byte[] EncodedBytes;   // Pre-encoded JPG/PNG image bytes
        public float Timestamp;       // Timestamp of capture
        public int FrameIndex;        // Sequential frame number
    }
}
