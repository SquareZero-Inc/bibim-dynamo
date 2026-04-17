// Copyright (c) 2026 SquareZero Inc. - Licensed under Apache 2.0. See LICENSE in the repo root.
using System;

namespace BIBIM_MVP
{
    public static class AppLanguage
    {
        public const string English = "en";
        public const string Korean = "kr";

        public static string Current { get; private set; } = Default;

        public static string Default
        {
            get
            {
#if APP_LANG_EN
                return English;
#else
                return Korean;
#endif
            }
        }

        public static bool IsEnglish => string.Equals(Current, English, StringComparison.OrdinalIgnoreCase);

        public static string Normalize(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return Default;
            }

            string normalized = language.Trim().ToLowerInvariant();
            if (normalized == "en" || normalized == "en-us" || normalized == "english")
            {
                return English;
            }

            if (normalized == "ko" || normalized == "ko-kr" || normalized == "kr" || normalized == "korean")
            {
                return Korean;
            }

            return Default;
        }

        public static void Initialize(string language = null)
        {
            Current = Normalize(language);
        }
    }
}
