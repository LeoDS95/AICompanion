using System;
using System.Collections.Generic;

namespace AICompanion
{
    /// <summary>
    /// Mod 配置
    /// </summary>
    public class ModConfig
    {
        // ── AI 人设 ──────────────────────────────────────────────────────
        public string AIName { get; set; } = "小助手";
        public string AIPersonality { get; set; } = "活泼可爱";

        // ── LLM 配置 ──────────────────────────────────────────────────────
        public string LLMProvider { get; set; } = "OpenAI";
        public string APIKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o";
        public string BaseURL { get; set; } = "https://api.openai.com/v1";

        // ── 行为配置 ──────────────────────────────────────────────────────
        public bool AutoStartPython { get; set; } = true;
        public bool EnableCompanionMode { get; set; } = true;

        // ── 预设 LLM 提供商（2026-05 最新） ─────────────────────────────
        public static readonly Dictionary<string, LLMPreset> LLMPresets = new()
        {
            ["OpenAI"] = new LLMPreset
            {
                Name = "OpenAI",
                BaseURL = "https://api.openai.com/v1",
                DefaultModel = "gpt-4o",
                Models = new[] { "gpt-4o", "gpt-4o-mini", "gpt-5.5", "gpt-5.4-mini", "gpt-5.4-nano" }
            },
            ["Claude"] = new LLMPreset
            {
                Name = "Claude",
                BaseURL = "https://api.anthropic.com/v1",
                DefaultModel = "claude-sonnet-4-6",
                Models = new[] { "claude-opus-4-7", "claude-opus-4-6", "claude-sonnet-4-6", "claude-sonnet-4-5", "claude-haiku-4-5" }
            },
            ["Gemini"] = new LLMPreset
            {
                Name = "Gemini",
                BaseURL = "https://generativelanguage.googleapis.com/v1beta",
                DefaultModel = "gemini-2.5-flash",
                Models = new[] { "gemini-3.1-pro", "gemini-3.5-flash", "gemini-2.5-flash", "gemini-2.5-pro" }
            },
            ["XAI"] = new LLMPreset
            {
                Name = "XAI",
                BaseURL = "https://api.x.ai/v1",
                DefaultModel = "grok-4.3",
                Models = new[] { "grok-4.3", "grok-build-0.1" }
            },
            ["DeepSeek"] = new LLMPreset
            {
                Name = "DeepSeek",
                BaseURL = "https://api.deepseek.com",
                DefaultModel = "deepseek-v4-flash",
                Models = new[] { "deepseek-v4-pro", "deepseek-v4-flash" }
            },
            ["MiMo"] = new LLMPreset
            {
                Name = "MiMo (Credits)",
                BaseURL = "https://api.xiaomimimo.com/v1",
                DefaultModel = "mimo-v2.5-pro",
                Models = new[] { "mimo-v2.5-pro", "mimo-v2.5", "mimo-v2-flash" }
            },
            ["MiMo Plan"] = new LLMPreset
            {
                Name = "MiMo (Token Plan)",
                BaseURL = "https://token-plan-cn.xiaomimimo.com/v1",
                DefaultModel = "mimo-v2.5-pro",
                Models = new[] { "mimo-v2.5-pro", "mimo-v2.5", "mimo-v2-flash" }
            },
            ["自定义"] = new LLMPreset
            {
                Name = "自定义",
                BaseURL = "",
                DefaultModel = "",
                Models = Array.Empty<string>()
            }
        };

        /// <summary>
        /// 应用预设
        /// </summary>
        public void ApplyPreset(string provider)
        {
            if (LLMPresets.TryGetValue(provider, out var preset))
            {
                LLMProvider = provider;
                BaseURL = preset.BaseURL;
                Model = preset.DefaultModel;
            }
        }
    }

    /// <summary>
    /// LLM 预设配置
    /// </summary>
    public class LLMPreset
    {
        public string Name { get; set; }
        public string BaseURL { get; set; }
        public string DefaultModel { get; set; }
        public string[] Models { get; set; }
    }
}
