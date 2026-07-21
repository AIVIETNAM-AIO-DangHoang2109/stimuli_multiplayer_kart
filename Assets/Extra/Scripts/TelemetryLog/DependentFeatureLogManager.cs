using System;
using UnityEngine;

namespace Extra.TelemetryLog
{
    /// <summary>
    /// Game-specific feature logger stub implementing <see cref="ITelemetryProvider"/>.
    /// Acts as an extension point for game-specific telemetry features to be implemented per-project.
    /// To implement, modify this class to declare game-specific CSV headers, collect frame-level data, 
    /// and return computed snapshots at the 250 ms ticks.
    /// </summary>
    [DisallowMultipleComponent]
    public class DependentFeatureLogManager : MonoBehaviour, ITelemetryProvider
    {
        /// <summary>
        /// Returns the game-specific CSV header columns.
        /// Returns an empty array by default until extended.
        /// </summary>
        public string[] GetHeaders()
        {
            return Array.Empty<string>();
        }

        /// <summary>
        /// Called every frame to accumulate game-specific state (e.g. drift duration, lap progress, etc.).
        /// No-op by default until extended.
        /// </summary>
        public void CollectFrameData()
        {
            // Extension point: implement per-game logic here
        }

        /// <summary>
        /// Called every 250 ms tick to flush and return the game-specific snapshots.
        /// Returns an empty array by default until extended.
        /// </summary>
        public string[] FlushAndGetSnapshot()
        {
            return Array.Empty<string>();
        }
    }
}
