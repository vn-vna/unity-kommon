using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.Common.Integration.L18n
{
    public static class LangCode
    {
        // A handful of convenient named constants
        public const string English = "en";
        public const string Spanish = "es";
        public const string French = "fr";
        public const string German = "de";
        public const string Chinese = "zh";
        public const string Japanese = "ja";
        public const string Russian = "ru";
        public const string Arabic = "ar";
        public const string Portuguese = "pt";
        public const string Italian = "it";
        public const string Dutch = "nl";
        public const string Korean = "ko";
        public const string Turkish = "tr";
        public const string Swedish = "sv";
        public const string Danish = "da";
        public const string NorwegianBokmal = "nb";
        public const string NorwegianNynorsk = "nn";
        public const string Finnish = "fi";
        public const string Polish = "pl";
        public const string Czech = "cs";
        public const string Greek = "el";
        public const string Hebrew = "he";
        public const string Hindi = "hi";
        public const string Bengali = "bn";
        public const string Urdu = "ur";
        public const string Persian = "fa";
        public const string Indonesian = "id";
        public const string Malay = "ms";
        public const string Thai = "th";
        public const string Vietnamese = "vi";
        public const string Romanian = "ro";
        public const string Hungarian = "hu";
        public const string Bulgarian = "bg";
        public const string Ukrainian = "uk";
        public const string Serbian = "sr";
        public const string Croatian = "hr";
        public const string Slovak = "sk";
        public const string Slovenian = "sl";
        public const string Catalan = "ca";
        public const string Afrikaans = "af";
        public const string Swahili = "sw";

        public static readonly string[] AllCodes = new[]
        {
            English, Spanish, French, German, Chinese, Japanese,
            Russian, Arabic, Portuguese, Italian, Dutch, Korean,
            Turkish, Swedish, NorwegianBokmal, NorwegianNynorsk,
            Finnish, Polish, Czech, Greek, Hebrew, Hindi, Bengali,
            Urdu, Persian, Indonesian, Malay, Thai, Vietnamese,
            Romanian, Hungarian, Ukrainian, Serbian, Croatian,
            Slovak, Slovenian, Catalan, Afrikaans, Swahili,
        };

        private static readonly IReadOnlyDictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { English,          "English" },
                { Spanish,          "Spanish" },
                { French,           "French" },
                { German,           "German" },
                { Chinese,          "Chinese" },
                { Japanese,         "Japanese" },
                { Russian,          "Russian" },
                { Arabic,           "Arabic" },
                { Portuguese,       "Portuguese" },
                { Italian,          "Italian" },
                { Dutch,            "Dutch" },
                { Korean,           "Korean" },
                { Turkish,          "Turkish" },
                { Swedish,          "Swedish" },
                { NorwegianBokmal,  "Norwegian (Bokmål)" },
                { NorwegianNynorsk, "Norwegian (Nynorsk)" },
                { Finnish,          "Finnish" },
                { Polish,           "Polish" },
                { Czech,            "Czech" },
                { Greek,            "Greek" },
                { Hebrew,           "Hebrew" },
                { Hindi,            "Hindi" },
                { Bengali,          "Bengali" },
                { Urdu,             "Urdu" },
                { Persian,          "Persian" },
                { Indonesian,       "Indonesian" },
                { Malay,            "Malay" },
                { Thai,             "Thai" },
                { Vietnamese,       "Vietnamese" },
                { Romanian,         "Romanian" },
                { Hungarian,        "Hungarian" },
                { Ukrainian,        "Ukrainian" },
                { Serbian,          "Serbian" },
                { Croatian,         "Croatian" },
                { Slovak,           "Slovak" },
                { Slovenian,        "Slovenian" },
                { Catalan,          "Catalan" },
                { Afrikaans,        "Afrikaans" },
                { Swahili,          "Swahili" }
            };

        public static bool IsValid(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            return AllCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryGetDisplayName(string code, out string displayName)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                displayName = null;
                return false;
            }

            return DisplayNames.TryGetValue(code, out displayName);
        }

        public static IReadOnlyList<string> GetAllCodes() => Array.AsReadOnly(AllCodes);
    }

    public class CountryCode
    {
        public const string UnitedStates = "us";
    }

    public class LanguageInfo
    {
        public string LanguageCode => _languageCode;
        public string CountryCode => _countryCode;

        private readonly string _languageCode;
        private readonly string _countryCode;

        public LanguageInfo(string languageCode, string countryCode)
        {
            _languageCode = languageCode;
            _countryCode = countryCode;
        }
    }

    public class LocalizationEntry
    {
        public string this[string key] =>
            _localizedValues.TryGetValue(key, out string value)
                ? value
                : null;

        private readonly Dictionary<string, string> _localizedValues;
        private readonly LanguageInfo _languageInfo;

        public LocalizationEntry(
            LanguageInfo languageInfo,
            Dictionary<string, string> localizedValues
        )
        {
            _languageInfo = languageInfo;
            _localizedValues = localizedValues;
        }

    }

    public interface ILocalizationProvider
    {
        LocalizationEntry[] LocalizationEntries { get; }
        bool IsInitialized { get; }

        void Initialize();
        string AcquireLocalizedValue(string language, string localizationKey);
    }

    public class LocalizationLoader
    {
        public static LocalizationEntry ParseFromString(string data)
        {
            LocalizationEntry entry = null;

            try
            {
                using StringReader reader = new StringReader(data);
                entry = ParseLangEntry(reader);
                return entry;

            }
            catch (IOException ioex)
            {
                QuickLog.Error<LocalizationLoader>(
                    $"Error while parsing localization data due to IO Exception: {ioex}"
                );
            }
            catch (FormatException fex)
            {
                QuickLog.Error<LocalizationLoader>(
                    $"Error while parsing localization data due to error format: {fex}"
                );
            }

            return entry;
        }

        private static LocalizationEntry ParseLangEntry(StringReader reader)
        {
            Dictionary<string, string> localizedValues = new Dictionary<string, string>();
            string currentSection = null;
            string currentLangCode = null;
            string currentCountryCode = null;

            while (reader.ReadLine() is string line)
            {
                line = line.Trim();
                if (!ValidateLine(ref currentSection, line)) continue;
                ParseSingleLine(
                    localizedValues, currentSection,
                    ref currentLangCode, ref currentCountryCode, line
                );
            }

            if (string.IsNullOrEmpty(currentLangCode))
            {
                throw new FormatException(
                    "Language code is not specified in localization data."
                );
            }

            if (string.IsNullOrEmpty(currentCountryCode))
            {
                throw new FormatException(
                    "Country code is not specified in localization data."
                );
            }

            return new LocalizationEntry(
                new LanguageInfo(currentLangCode, currentCountryCode),
                localizedValues
            );
        }

        private static bool ValidateLine(ref string currentSection, string line)
        {
            if (line.Length == 0) return false;
            if (line.StartsWith("#")) return false;
            if (line.StartsWith(";")) return false;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                string sectionName = line[1..^1].Trim();
                currentSection = sectionName;
                return false;
            }

            return false;
        }

        private static void ParseSingleLine(
            Dictionary<string, string> localizedValues,
            string currentSection,
            ref string currentLangCode,
            ref string currentCountryCode,
            string line
        )
        {
            switch (currentSection)
            {
                case "configuration":
                    ParseConfigurationValue(ref currentLangCode, ref currentCountryCode, line);
                    break;

                case "translations":
                    ParseTranslationValue(localizedValues, line);
                    break;

                default:
                    throw new FormatException($"Unknown section: '{currentSection}'");
            }
        }

        private static void ParseTranslationValue(Dictionary<string, string> localizedValues, string line)
        {
            string[] kvp = line.Split('=', 2);
            if (kvp.Length != 2)
            {
                throw new FormatException($"Invalid translation line: '{line}'");
            }

            string key = kvp[0].Trim();
            string value = kvp[1].Trim();

            localizedValues[key] = value;
        }

        private static void ParseConfigurationValue(ref string currentLangCode, ref string currentCountryCode, string line)
        {
            string[] kvp = line.Split('=', 2);
            if (kvp.Length != 2)
            {
                throw new FormatException($"Invalid configuration line: '{line}'");
            }

            string key = kvp[0].Trim();
            string value = kvp[1].Trim();

            switch (key)
            {
                case "language_code":
                    currentLangCode = value;
                    break;
                case "country_code":
                    currentCountryCode = value;
                    break;
                default:
                    throw new FormatException($"Unknown configuration key: '{key}'");
            }
        }
    }

    public class LocalizationConfig :
        ScriptableObject
    {
    }

    public class DefaultLocalizationProvider : ILocalizationProvider
    {
        public LocalizationEntry[] LocalizationEntries => new LocalizationEntry[0];
        public bool IsInitialized => true;

        public void Initialize() { }

        public string AcquireLocalizedValue(string language, string localizationKey)
        {
            return null;
        }
    }

    public class RemoteLocalizationProvider : ILocalizationProvider
    {
        public LocalizationEntry[] LocalizationEntries => new LocalizationEntry[0];
        public bool IsInitialized => true;

        public void Initialize() { }

        public string AcquireLocalizedValue(string language, string localizationKey)
        {
            return null;
        }
    }

    public interface ILocalizationManager
    {
    }

    public abstract class LocalizationManager<T> :
        SingletonBehavior<T>,
        ILocalizationManager
        where T : LocalizationManager<T>
    {
        protected override void Awake()
        {
            base.Awake();
            Integration.RegisterManager(this);
        }

        public void Initialize()
        {
            StartCoroutine(InitializeCoroutine());
        }

        public IEnumerator InitializeCoroutine()
        {
            yield break;
        }

        public void ConfigureFallbackLanguage(string languageCode)
        {
        }
    }
}