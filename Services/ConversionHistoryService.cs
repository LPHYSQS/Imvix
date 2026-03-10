using Imvix.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Imvix.Services
{
    public sealed class ConversionHistoryService
    {
        private const int MaxEntries = 12;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly object _gate = new();
        private readonly string _historyPath;

        public ConversionHistoryService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsDirectory = Path.Combine(appDataPath, "Imvix");
            Directory.CreateDirectory(settingsDirectory);
            _historyPath = Path.Combine(settingsDirectory, "history.json");
        }

        public IReadOnlyList<ConversionHistoryEntry> Load()
        {
            lock (_gate)
            {
                return NormalizeEntries(LoadInternal());
            }
        }

        public IReadOnlyList<ConversionHistoryEntry> Append(ConversionHistoryEntry entry)
        {
            lock (_gate)
            {
                var entries = LoadInternal();
                entries.Insert(0, entry);
                entries = NormalizeEntries(entries);

                var json = JsonSerializer.Serialize(entries, JsonOptions);
                File.WriteAllText(_historyPath, json);
                return entries;
            }
        }

        public IReadOnlyList<ConversionHistoryEntry> Clear()
        {
            lock (_gate)
            {
                var empty = new List<ConversionHistoryEntry>();
                var json = JsonSerializer.Serialize(empty, JsonOptions);
                File.WriteAllText(_historyPath, json);
                return empty;
            }
        }

        private List<ConversionHistoryEntry> LoadInternal()
        {
            try
            {
                if (!File.Exists(_historyPath))
                {
                    return [];
                }

                var json = File.ReadAllText(_historyPath);
                return JsonSerializer.Deserialize<List<ConversionHistoryEntry>>(json, JsonOptions)
                       ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static List<ConversionHistoryEntry> NormalizeEntries(IEnumerable<ConversionHistoryEntry> entries)
        {
            return entries
                .OrderByDescending(entry => entry.Timestamp)
                .Take(MaxEntries)
                .ToList();
        }
    }
}
