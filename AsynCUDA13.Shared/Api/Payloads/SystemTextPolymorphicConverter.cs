using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsynCUDA13.Shared.Api.Payloads
{
    /// <summary>
    /// Generic polymorphic JsonConverter for System.Text.Json.
    /// Discovers concrete implementations of the target interface via Reflection
    /// and selects the correct type based on configurable property-name heuristics.
    /// </summary>
    public class SystemTextPolymorphicConverter<TInterface> : JsonConverter<TInterface>
        where TInterface : class
    {
        private static readonly Dictionary<string, List<Type>> _implementationCache = new();
        private static readonly object _cacheLock = new();

        private readonly string[] _primaryMarkers;
        private readonly string? _fallbackProperty;
        private readonly string? _fallbackValue;

        /// <param name="primaryMarkers">Property names that indicate the "first" concrete type (case-insensitive match). If any is present, the first implementation is chosen.</param>
        /// <param name="fallbackProperty">Optional property to inspect for a fallback value check.</param>
        /// <param name="fallbackValue">If set, the first implementation is chosen when this property equals this value (case-insensitive).</param>
        public SystemTextPolymorphicConverter(string[] primaryMarkers, string? fallbackProperty = null, string? fallbackValue = null)
        {
            this._primaryMarkers = primaryMarkers;
            this._fallbackProperty = fallbackProperty;
            this._fallbackValue = fallbackValue;
        }

        private static List<Type> GetImplementations()
        {
            lock (_cacheLock)
            {
                if (!_implementationCache.TryGetValue(typeof(TInterface).FullName!, out var cached))
                {
                    var assembly = typeof(TInterface).Assembly;
                    cached = assembly.GetTypes()
                        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(TInterface).IsAssignableFrom(t))
                        .ToList();
                    _implementationCache[typeof(TInterface).FullName!] = cached;
                }

                return cached;
            }
        }

        public override TInterface? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            string json = root.GetRawText();

            var implementations = GetImplementations();
            if (implementations.Count <= 1)
            {
                return JsonSerializer.Deserialize<TInterface>(json, options);
            }

            bool matchesFirst = this.HasMarker(root) || this.MatchesFallback(root);

            Type target = matchesFirst ? implementations[0] : implementations[^1];
            return (TInterface?) JsonSerializer.Deserialize(json, target, options)!;
        }

        private bool HasMarker(JsonElement element)
        {
            foreach (string marker in this._primaryMarkers)
            {
                if (element.TryGetProperty(marker, out _))
                {
                    return true;
                }
            }

            // Check nested "Info" object for markers (e.g., IMediaData → Info: IMediaInfo)
            if (element.TryGetProperty("Info", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                foreach (string marker in this._primaryMarkers)
                {
                    if (info.TryGetProperty(marker, out _))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool MatchesFallback(JsonElement element)
        {
            if (this._fallbackProperty == null || this._fallbackValue == null)
            {
                return false;
            }

            return element.TryGetProperty(this._fallbackProperty, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                string.Equals(prop.GetString(), this._fallbackValue, StringComparison.OrdinalIgnoreCase);
        }

        public override void Write(Utf8JsonWriter writer, TInterface value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
