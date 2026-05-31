using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace AICompanion
{
    /// <summary>
    /// 聊天广播器 - 尝试让 AI 消息在主机聊天框显示
    /// 
    /// 当前方案：AI 写 reply.json → 主机读取 → addMessage 显示
    /// 
    /// 已尝试但失败的方案：
    /// 1. Game1.chatBox.addMessage() → 只在本地显示，不同步到主机
    /// 2. helper.Multiplayer.SendMessage() → SMAPI Mod 间通信，不触发游戏聊天框
    /// 3. sendChatMessage(LanguageCode, String, Int64) → 广播成功但两边都没显示
    /// 
    /// 待验证：
    /// - 主机实例是否真的在运行 OnHostTick？
    /// - reply.json 是否被正确读取？
    /// - addMessage 是否在主机端生效？
    /// </summary>
    public static class ChatBroadcaster
    {
        private static IMonitor _monitor;

        public static void Init(IMonitor monitor)
        {
            _monitor = monitor;
        }

        /// <summary>
        /// 发送消息（当前方案：写入 reply.json）
        /// </summary>
        public static bool SendMessage(string text)
        {
            return SendViaReply(text);
        }

        /// <summary>
        /// 写入 reply.json，让主机实例读取并显示
        /// 
        /// 文件格式：
        /// {
        ///   "text": "消息内容",
        ///   "timestamp": 1234567890
        /// }
        /// </summary>
        public static bool SendViaReply(string text)
        {
            try
            {
                var replyFile = Path.Combine(GameConfig.AIDir, "reply.json");
                var payload = JsonSerializer.Serialize(new
                {
                    text = text,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                File.WriteAllText(replyFile, payload);
                _monitor?.Log($"[ChatBroadcaster] 写入reply.json: {text}", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[ChatBroadcaster] 写入失败: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 诊断：打印 Multiplayer 所有 chat/message 相关方法
        /// 用于查找正确的 API 签名
        /// </summary>
        public static void Diagnose()
        {
            try
            {
                var multiplayerField = typeof(Game1).GetField(
                    "multiplayer",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                );
                var multiplayer = multiplayerField?.GetValue(null);
                if (multiplayer == null)
                {
                    _monitor?.Log("[诊断] multiplayer 为 null", LogLevel.Warn);
                    return;
                }

                _monitor?.Log("[诊断] 开始扫描 Multiplayer 方法...", LogLevel.Info);
                var methods = multiplayer.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                foreach (var m in methods)
                {
                    if (m.Name.ToLower().Contains("chat") || m.Name.ToLower().Contains("message"))
                    {
                        var ps = string.Join(", ",
                            Enumerable.Select(m.GetParameters(), p => $"{p.ParameterType.Name} {p.Name}")
                        );
                        _monitor?.Log($"[方法] {m.Name}({ps})", LogLevel.Info);
                    }
                }
                _monitor?.Log("[诊断] 扫描完成", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[诊断] 失败: {ex.Message}", LogLevel.Warn);
            }
        }
    }
}
