using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AsynCUDA13.Shared.Api.Payloads
{
    /// <summary>
    /// Generic polymorphic JsonConverter for Newtonsoft.Json.
    /// Discovers concrete implementations of the target interface via Reflection
    /// and selects the correct type based on configurable property-name heuristics.
    /// </summary>
    public class NewtonsoftPolymorphicConverter<TInterface> : JsonConverter
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
        public NewtonsoftPolymorphicConverter(string[] primaryMarkers, string? fallbackProperty = null, string? fallbackValue = null)
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

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(TInterface);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            var implementations = GetImplementations();
            if (implementations.Count == 0)
            {
                throw new JsonSerializationException($"No concrete implementation of {typeof(TInterface).FullName} was found.");
            }

            Type target;
            if (implementations.Count == 1)
            {
                target = implementations[0];
            }
            else
            {
                bool matchesFirst = this.HasMarker(token) || this.MatchesFallback(token);
                target = matchesFirst ? implementations[0] : implementations[^1];
            }

            var result = Activator.CreateInstance(target)
                ?? throw new JsonSerializationException($"Could not create an instance of {target.FullName}.");

            using var tokenReader = token.CreateReader();
            serializer.Populate(tokenReader, result);

            return result;
        }

        private bool HasMarker(JToken token)
        {
            if (token is not JObject obj)
            {
                return false;
            }

            foreach (string marker in this._primaryMarkers)
            {
                if (obj[marker] != null)
                {
                    return true;
                }
            }

            // Check nested "Info" object for markers (e.g., IMediaData → Info: IMediaInfo)
            if (obj["Info"] is JObject infoObj)
            {
                foreach (string marker in this._primaryMarkers)
                {
                    if (infoObj[marker] != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool MatchesFallback(JToken token)
        {
            if (this._fallbackProperty == null || this._fallbackValue == null || token is not JObject obj)
            {
                return false;
            }

            var prop = obj[this._fallbackProperty];
            return prop?.Type == JTokenType.String && string.Equals((string?) prop, this._fallbackValue, StringComparison.OrdinalIgnoreCase);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            serializer.Serialize(writer, value, value.GetType());
        }
    }
}
