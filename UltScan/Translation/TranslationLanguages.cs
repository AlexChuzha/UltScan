using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltScan;

public sealed record LanguageOption(string Code, string Name);

public static class TranslationLanguages
{
    private static readonly LanguageOption Auto = new("auto", "Auto");

    private static readonly IReadOnlyList<LanguageOption> Limited = new List<LanguageOption>
    {
        new("en", "English"),
        new("ru", "Russian"),
        new("zh", "Chinese"),
        new("ja", "Japanese")
    };

    private static readonly IReadOnlyList<LanguageOption> All = new List<LanguageOption>
    {
        new("af", "Afrikaans"),
        new("am", "Amharic"),
        new("ar", "Arabic"),
        new("az", "Azerbaijani"),
        new("be", "Belarusian"),
        new("bg", "Bulgarian"),
        new("bn", "Bengali"),
        new("bs", "Bosnian"),
        new("ca", "Catalan"),
        new("ceb", "Cebuano"),
        new("co", "Corsican"),
        new("cs", "Czech"),
        new("cy", "Welsh"),
        new("da", "Danish"),
        new("de", "German"),
        new("el", "Greek"),
        new("en", "English"),
        new("eo", "Esperanto"),
        new("es", "Spanish"),
        new("et", "Estonian"),
        new("eu", "Basque"),
        new("fa", "Persian"),
        new("fi", "Finnish"),
        new("fr", "French"),
        new("fy", "Frisian"),
        new("ga", "Irish"),
        new("gd", "Scots Gaelic"),
        new("gl", "Galician"),
        new("gu", "Gujarati"),
        new("ha", "Hausa"),
        new("haw", "Hawaiian"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("hmn", "Hmong"),
        new("hr", "Croatian"),
        new("ht", "Haitian Creole"),
        new("hu", "Hungarian"),
        new("hy", "Armenian"),
        new("id", "Indonesian"),
        new("ig", "Igbo"),
        new("is", "Icelandic"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("jv", "Javanese"),
        new("ka", "Georgian"),
        new("kk", "Kazakh"),
        new("km", "Khmer"),
        new("kn", "Kannada"),
        new("ko", "Korean"),
        new("ku", "Kurdish"),
        new("ky", "Kyrgyz"),
        new("la", "Latin"),
        new("lb", "Luxembourgish"),
        new("lo", "Lao"),
        new("lt", "Lithuanian"),
        new("lv", "Latvian"),
        new("mg", "Malagasy"),
        new("mi", "Maori"),
        new("mk", "Macedonian"),
        new("ml", "Malayalam"),
        new("mn", "Mongolian"),
        new("mr", "Marathi"),
        new("ms", "Malay"),
        new("mt", "Maltese"),
        new("my", "Myanmar (Burmese)"),
        new("ne", "Nepali"),
        new("nl", "Dutch"),
        new("no", "Norwegian"),
        new("ny", "Chichewa"),
        new("pa", "Punjabi"),
        new("pl", "Polish"),
        new("ps", "Pashto"),
        new("pt", "Portuguese"),
        new("ro", "Romanian"),
        new("ru", "Russian"),
        new("sd", "Sindhi"),
        new("si", "Sinhala"),
        new("sk", "Slovak"),
        new("sl", "Slovenian"),
        new("sm", "Samoan"),
        new("sn", "Shona"),
        new("so", "Somali"),
        new("sq", "Albanian"),
        new("sr", "Serbian"),
        new("st", "Sesotho"),
        new("su", "Sundanese"),
        new("sv", "Swedish"),
        new("sw", "Swahili"),
        new("ta", "Tamil"),
        new("te", "Telugu"),
        new("tg", "Tajik"),
        new("th", "Thai"),
        new("tl", "Filipino"),
        new("tr", "Turkish"),
        new("uk", "Ukrainian"),
        new("ur", "Urdu"),
        new("uz", "Uzbek"),
        new("vi", "Vietnamese"),
        new("xh", "Xhosa"),
        new("yi", "Yiddish"),
        new("yo", "Yoruba"),
        new("zh", "Chinese"),
        new("zu", "Zulu")
    };

    public static IReadOnlyList<LanguageOption> GetLanguages(bool includeAll, bool includeAuto)
    {
        var baseList = includeAll ? All : Limited;
        if (!includeAuto)
        {
            return baseList;
        }

        var list = new List<LanguageOption>(baseList.Count + 1) { Auto };
        list.AddRange(baseList);
        return list;
    }

    public static string GetBestTargetLanguage(CultureInfo culture)
    {
        var code = culture.TwoLetterISOLanguageName;
        if (All.Any(l => l.Code == code))
        {
            return code;
        }

        return "en";
    }
}
