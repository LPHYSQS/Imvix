using CommunityToolkit.Mvvm.ComponentModel;

namespace ImvixPro.Models
{
    public sealed partial class LanguageOption : ObservableObject
    {
        public LanguageOption(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public string Code { get; }

        [ObservableProperty]
        private string displayName;

        public override string ToString() => DisplayName;
    }
}
