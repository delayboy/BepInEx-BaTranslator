using System;
using System.Collections.Concurrent;
using System.Text;
using HarmonyLib;
using OpenCCNET;

namespace MuvluvUiTranslate;

/// <summary>
/// BA 文本入口:NGUI UILabel.set_text(dump.cs RVA 0x25C6BF0)。
/// 职责单一:繁→简转换。原词典/捕获/翻译 worker 体系已移除。
/// </summary>
internal static class TextFlow
{
    /// <summary>原文→译文缓存。打字机/逐帧重设同一串时只查表不重转。</summary>
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>含 CJK 汉字(繁简都在 \u4E00-\u9FFF)才进入转换,纯 ASCII/假名/标点直接返回。</summary>
    public static void ProcessIncoming(ref string value)
    {
        try
        {
            if (string.IsNullOrEmpty(value) || !Config.TranslationEnabled.Value)
                return;
            if (!ContainsHan(value))
                return;

            // TWToHans 等价 python OpenCC('t2s.json'),内部走 TWVariants+TWPhrases+STCharacters+STPhrases
            value = Cache.GetOrAdd(value, k => ZhConverter.TWToHans(k, false));
        }
        catch
        {
            /* 钩子内异常绝不影响游戏渲染 */
        }
    }

    private static bool ContainsHan(string s)
    {
        foreach (var c in s)
            if (c >= '\u4E00' && c <= '\u9FFF')
                return true;
        return false;
    }
}

/// <summary>
/// UILabel.text 属性 setter:BA 全部代码设值路径的唯一入口。
/// Prefix 改写 ref value 后,原 setter 用简体写回。
/// </summary>
[HarmonyPatch(typeof(UILabel), "set_text")]
internal static class UILabel_SetTextPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref string value) => TextFlow.ProcessIncoming(ref value);
}