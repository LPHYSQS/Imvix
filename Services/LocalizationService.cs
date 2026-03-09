using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Imvix.Services
{
    public sealed class LocalizationService
    {
        private const string FallbackLanguage = "en-US";

        private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> _active = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _fallback = new(StringComparer.OrdinalIgnoreCase);

        public string CurrentLanguageCode { get; private set; } = "zh-CN";

        public void SetLanguage(string languageCode)
        {
            var dictionary = LoadLanguage(languageCode);
            var fallback = LoadLanguage(FallbackLanguage);
            if (dictionary.Count == 0)
            {
                dictionary = fallback;
                languageCode = FallbackLanguage;
            }

            _active = dictionary;
            _fallback = fallback;
            CurrentLanguageCode = languageCode;
        }

        public string Translate(string key)
        {
            if (_active.TryGetValue(key, out var value))
            {
                return value;
            }

            if (_fallback.TryGetValue(key, out var fallbackValue))
            {
                return fallbackValue;
            }

            return key;
        }

        private Dictionary<string, string> LoadLanguage(string languageCode)
        {
            if (_cache.TryGetValue(languageCode, out var cached))
            {
                return cached;
            }

            try
            {
                var uri = new Uri($"avares://Imvix/Assets/Localization/{languageCode}.json");
                if (!AssetLoader.Exists(uri))
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                using var stream = AssetLoader.Open(uri);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                                 ?? new Dictionary<string, string>();

                var normalized = new Dictionary<string, string>(dictionary, StringComparer.OrdinalIgnoreCase);
                _cache[languageCode] = normalized;
                return normalized;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
