// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
#if NET48
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#else
using System.Text.Json;
using System.Text.Encodings.Web;
#endif

namespace BIBIM_MVP
{
    /// <summary>
    /// Cross-platform JSON serialization helper for .NET 4.8 and .NET 8
    /// Abstracts the differences between Newtonsoft.Json and System.Text.Json
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>
        /// Serialize an object to JSON string
        /// </summary>
        /// <typeparam name="T">Type of object to serialize</typeparam>
        /// <param name="obj">Object to serialize</param>
        /// <param name="indented">Whether to format the output with indentation (default: false)</param>
        /// <returns>JSON string representation of the object</returns>
        public static string Serialize<T>(T obj, bool indented = false)
        {
            if (obj == null)
                return null;

#if NET48
            return JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None);
#else
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions 
            { 
                WriteIndented = indented,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
#endif
        }

        /// <summary>
        /// Deserialize a JSON string to an object
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize to</typeparam>
        /// <param name="json">JSON string to deserialize</param>
        /// <returns>Deserialized object of type T</returns>
        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
                return default(T);

#if NET48
            return JsonConvert.DeserializeObject<T>(json);
#else
            return JsonSerializer.Deserialize<T>(json);
#endif
        }

        /// <summary>
        /// Serialize with camelCase property names (useful for APIs)
        /// </summary>
        /// <typeparam name="T">Type of object to serialize</typeparam>
        /// <param name="obj">Object to serialize</param>
        /// <param name="indented">Whether to format the output with indentation</param>
        /// <returns>JSON string with camelCase property names</returns>
        public static string SerializeCamelCase<T>(T obj, bool indented = false)
        {
            if (obj == null)
                return null;

#if NET48
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                Formatting = indented ? Formatting.Indented : Formatting.None
            };
            return JsonConvert.SerializeObject(obj, settings);
#else
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = indented,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return JsonSerializer.Serialize(obj, options);
#endif
        }

        /// <summary>
        /// Try to deserialize JSON string, returns default(T) on failure
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize to</typeparam>
        /// <param name="json">JSON string to deserialize</param>
        /// <param name="result">Deserialized object (out parameter)</param>
        /// <returns>True if deserialization succeeded, false otherwise</returns>
        public static bool TryDeserialize<T>(string json, out T result)
        {
            try
            {
                result = Deserialize<T>(json);
                return true;
            }
            catch
            {
                result = default(T);
                return false;
            }
        }

        /// <summary>
        /// Parse JSON string to dynamic object (JObject for .NET 4.8, JsonDocument for .NET 8)
        /// </summary>
        /// <param name="json">JSON string to parse</param>
        /// <returns>Dynamic JSON object</returns>
        public static object ParseDynamic(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

#if NET48
            return JObject.Parse(json);
#else
            return JsonDocument.Parse(json);
#endif
        }
    }
}
