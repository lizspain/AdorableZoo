using System;

namespace RainbowZoo.Core
{
    /// <summary>
    /// The single, zoo-wide Care Meter -- not tracked per animal. ZooManager is the sole
    /// runtime owner/writer of this state (one-way data flow -- see Technical Design doc).
    /// </summary>
    [Serializable]
    public sealed class ZooCareMeterState
    {
        public int currentHearts;
        public int currentThreshold;

        public bool IsComplete => currentHearts >= currentThreshold;

        public void AddHearts(int amount)
        {
            currentHearts += amount;
        }

        /// <summary>Called by ZooManager after a Care Meter completion, once the next animal has been counted.</summary>
        public void StartNextCycle(int newThreshold)
        {
            currentHearts = 0;
            currentThreshold = newThreshold;
        }
    }
}
