namespace DisplayModeSwitcher
{
    public class DisplayMode
    {
        public string Label { get; init; } = string.Empty;
        public uint Width { get; init; }
        public uint Height { get; init; }
        public uint Frequency { get; init; }
    
        public override string ToString()
        {
            return Label;
        }
    
        public override bool Equals(object? obj)
        {
            return obj is DisplayMode other &&
                   Width == other.Width &&
                   Height == other.Height &&
                   Frequency == other.Frequency;
        }
    
        public override int GetHashCode()
        {
            return HashCode.Combine(Width, Height, Frequency);
        }
    }

    public static class DisplayModeEquivalence
    {
        public static bool AreEquivalent(DisplayMode? first, DisplayMode? second, uint frequencyTolerance = 1)
        {
            if (first is null || second is null)
                return false;

            return first.Width == second.Width &&
                   first.Height == second.Height &&
                   Math.Abs((long)first.Frequency - second.Frequency) <= frequencyTolerance;
        }
    }
}
