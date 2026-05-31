using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace AICompanion
{
    /// <summary>
    /// Generic Mod Config Menu API 接口（v1.16.0）
    /// https://github.com/spacechase0/GenericModConfigMenu
    /// </summary>
    public interface IGenericModConfigMenuApi
    {
        /// <summary>
        /// 注册一个 mod 的配置页面
        /// </summary>
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        /// <summary>
        /// 添加分节标题
        /// </summary>
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);

        /// <summary>
        /// 添加段落文本
        /// </summary>
        void AddParagraph(IManifest mod, Func<string> text);

        /// <summary>
        /// 添加布尔选项
        /// </summary>
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue,
            Func<string> name, Func<string> tooltip = null, string fieldId = null);

        /// <summary>
        /// 添加文本选项（下拉选择）
        /// </summary>
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue,
            Func<string> name, Func<string> tooltip = null, string[] allowedValues = null,
            Func<string, string> formatAllowedValue = null, string fieldId = null);

        /// <summary>
        /// 添加整数选项
        /// </summary>
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue,
            Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null,
            int? interval = null, string fieldId = null);

        /// <summary>
        /// 添加浮点数选项
        /// </summary>
        void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue,
            Func<string> name, Func<string> tooltip = null, float? min = null, float? max = null,
            float? interval = null, string fieldId = null);
    }
}
