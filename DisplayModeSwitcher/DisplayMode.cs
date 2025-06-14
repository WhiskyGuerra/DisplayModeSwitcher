namespace DisplayModeSwitcher
{
    public class DisplayMode
    {
        public string Label { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint Frequency { get; set; }
    
        public override string ToString()
        {
            return Label;
        }
    
        public override bool Equals(object obj)
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
}
