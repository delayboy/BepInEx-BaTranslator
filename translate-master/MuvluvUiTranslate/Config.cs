using BepInEx.Configuration;

namespace MuvluvUiTranslate;

public static class Config
{
    public static ConfigEntry<bool> TranslationEnabled { get; private set; }
    private static ConfigFile _config;

    public static void Initialize(ConfigFile config)
    {
        _config = config;
        TranslationEnabled = config.Bind(
            "Translation",
            "Enabled",
            true,
            "启用繁→简转换。关闭则所有文本原样放行。"
        );
    }

    public static void Shutdown() => _config?.Save();
}