namespace ImvixPro.Models
{
    public sealed class ThemeOption
    {
        public ThemeOption(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public string Code { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
