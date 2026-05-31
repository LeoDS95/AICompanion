using System;
using StardewModdingAPI;

namespace AICompanion
{
    /// <summary>
    /// Generic Mod Config Menu API 接口
    /// https://github.com/spacechase0/StardewValleyMods/tree/main/GenericModConfigMenu
    /// </summary>
    public interface IGenericModConfigMenuApi
    {
        /// <summary>
        /// 注册一个 mod 的配置页面
        /// </summary>
        void Register(IManifest modManifest, Action reset, Action save, bool titleScreenOnly = false);

        /// <summary>
        /// 添加分节标题
        /// </summary>
        void AddSectionTitle(IManifest modManifest, Func<string> text, Func<string> tooltip = null);

        /// <summary>
        /// 添加文本选项
        /// </summary>
        void AddTextOption(IManifest modManifest, Func<string> name, Func<string> tooltip,
            Func<string> getValue, Action<string> setValue, string[] allowedValues = null);

        /// <summary>
        /// 添加布尔选项
        /// </summary>
        void AddBoolOption(IManifest modManifest, Func<string> name, Func<string> tooltip,
            Func<bool> getValue, Action<bool> setValue);

        /// <summary>
        /// 添加整数选项
        /// </summary>
        void AddNumberOption(IManifest modManifest, Func<string> name, Func<string> tooltip,
            Func<int> getValue, Action<int> setValue, int? min = null, int? max = null, int? interval = null);

        /// <summary>
        /// 添加浮点数选项
        /// </summary>
        void AddNumberOption(IManifest modManifest, Func<string> name, Func<string> tooltip,
            Func<float> getValue, Action<float> setValue, float? min = null, float? max = null, float? interval = null);
    }
}
