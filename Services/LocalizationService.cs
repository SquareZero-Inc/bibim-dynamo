// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
#if NET48
using Newtonsoft.Json;
#else
using System.Text.Json;
#endif

namespace BIBIM_MVP
{
    public static class LocalizationService
    {
        private static readonly object Sync = new object();
        private static Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _language = AppLanguage.Default;
        private static bool _initialized;

        public static string CurrentLanguage => _language;

        public static void Initialize(string language = null)
        {
            lock (Sync)
            {
                _language = AppLanguage.Normalize(language);
                _fallback = LoadDictionary(AppLanguage.English);
                _strings = _language == AppLanguage.English
                    ? _fallback
                    : LoadDictionary(_language);
                _initialized = true;
            }
        }

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            EnsureInitialized();

            if (_strings.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (_fallback.TryGetValue(key, out string fallback) && !string.IsNullOrEmpty(fallback))
            {
                return fallback;
            }

            return key;
        }

        public static string Format(string key, params object[] args)
        {
            string template = Get(key);
            if (args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            Initialize(AppLanguage.Default);
        }

        private static Dictionary<string, string> LoadDictionary(string language)
        {
            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                string path = Path.Combine(assemblyDir, "i18n", $"{language}.json");
                if (!File.Exists(path))
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                string json = File.ReadAllText(path);
#if NET48
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
#else
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
#endif
                return dict ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
