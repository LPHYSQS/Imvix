namespace ImvixPro.Models
{
    public sealed class ConversionFailure
    {
        public ConversionFailure(string fileName, string reason)
        {
            FileName = fileName;
            Reason = reason;
        }

        public string FileName { get; }

        public string Reason { get; }
    }
}
