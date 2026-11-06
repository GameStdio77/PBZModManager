using System.Globalization;

namespace PBZModManager;

/// <summary>
/// The program's languages. Korean is written in the code and is the key; the other languages are looked up
/// in <see cref="Table"/>, and a text missing there shows in Korean rather than not at all.
/// </summary>
public static partial class L
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("ko", "한국어"), ("en", "English"), ("zh", "简体中文"), ("ja", "日本語"),
    };

    /// <summary>The language in use; chosen once at start, a change takes effect on the next start.</summary>
    public static string Current { get; private set; } = "ko";

    /// <summary>Uses a saved choice, or the system's language when there is none.</summary>
    public static void Use(string? code) => Current = IsKnown(code) ? code! : FromSystem();

    public static bool IsKnown(string? code) => Languages.Any(l => l.Code == code);

    /// <summary>The Windows display language when it is one of ours, English otherwise.</summary>
    public static string FromSystem()
    {
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return IsKnown(code) ? code : "en";
    }

    /// <summary>A text in the current language.</summary>
    public static string T(string korean)
    {
        if (Current == "ko" || !Table.TryGetValue(korean, out var t)) return korean;
        var text = Current switch { "en" => t.En, "zh" => t.Zh, "ja" => t.Ja, _ => "" };
        return text.Length > 0 ? text : korean;
    }

    /// <summary>A text with values put in its {0}, {1}... places.</summary>
    public static string F(string korean, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(korean), args);

    /// <summary>Every text the table knows, for checking that each has all its languages.</summary>
    public static IReadOnlyDictionary<string, (string En, string Zh, string Ja)> All => Table;
}
