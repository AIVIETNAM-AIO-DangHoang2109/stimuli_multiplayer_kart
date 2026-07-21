namespace Extra.TelemetryLog
{
    public interface ITelemetryProvider
    {
        /// <summary>
        /// Returns the CSV header columns this provider contributes.
        /// Called once during initialization to construct the master header.
        /// </summary>
        string[] GetHeaders();

        /// <summary>
        /// Called every frame (from Update). Accumulates per-frame data
        /// into internal buffers for the current 250 ms window.
        /// </summary>
        void CollectFrameData();

        /// <summary>
        /// Called every 250 ms tick. Computes aggregated values from
        /// accumulated frame data, returns them as CSV-ready string values
        /// (one per header column, same order), then resets internal accumulators.
        /// </summary>
        string[] FlushAndGetSnapshot();
    }
}
