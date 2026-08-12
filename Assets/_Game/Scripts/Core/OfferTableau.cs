namespace RainbowZoo.Core
{
    /// <summary>
    /// Runtime-only (never persisted): the 3 candidate animals currently offered to the player.
    /// Built by OfferGenerator, consumed by the Selection Tableau UI.
    /// </summary>
    public sealed class OfferTableau
    {
        public const int SlotCount = 3;

        private readonly AnimalDefinition[] slots;

        public OfferTableau(AnimalDefinition slot0, AnimalDefinition slot1, AnimalDefinition slot2)
        {
            slots = new[] { slot0, slot1, slot2 };
        }

        public AnimalDefinition GetSlot(int index) => slots[index];
    }
}
