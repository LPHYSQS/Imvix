namespace ImvixPro.Models
{
    public sealed class EnumOption<T>
        where T : struct
    {
        public EnumOption(T value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public T Value { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
