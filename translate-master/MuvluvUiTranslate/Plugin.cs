using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OpenCCNET;

namespace MuvluvUiTranslate;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "benson.muvluvuitranslate";
    public const string PluginName = "MuvluvUiTranslate";
    public const string PluginVersion = "0.2.0";

    public static new ManualLogSource Log;
    public static string PluginDirectory;

    private Harmony _harmony;

    public override void Load()
    {
        Log = base.Log;
        PluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? Paths.PluginPath;

        MuvluvUiTranslate.Config.Initialize(base.Config);

        if (!InitOpenCC())
        {
            Log.LogError(
                "OpenCC 初始化失败,繁→简转换不可用。检查插件目录下 Dictionary/ 与 JiebaResource/ 是否完整。"
            );
            return; // 不挂 hook,游戏文本原样显示
        }

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"{PluginName} {PluginVersion} loaded. OpenCC t2s ready.");
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        Log.LogInfo($"{PluginName} unloaded");
        return base.Unload();
    }

    /// <summary>
    /// OpenCC 初始化:需要插件目录下的 Dictionary/ 和 JiebaResource/ 两个词库文件夹。
    /// Path.GetDirectoryName(Assembly.Location) 在 IL2CPP 下指向 plugins\MuvluvUiTranslate\。
    /// </summary>
    private static bool InitOpenCC()
    {
        try
        {
            var dictDir = Path.Combine(PluginDirectory, "Dictionary");
            var jiebaDir = Path.Combine(PluginDirectory, "JiebaResource");
            if (!Directory.Exists(dictDir) || !Directory.Exists(jiebaDir))
            {
                Log.LogError($"词库目录缺失: {dictDir} / {jiebaDir}");
                return false;
            }

            ZhConverter.Initialize(dictDir, jiebaDir, false, SegmentMode.MaxMatch);
            return true;
        }
        catch (Exception e)
        {
            Log.LogError($"OpenCC 初始化异常: {e.Message}");
            return false;
        }
    }
}