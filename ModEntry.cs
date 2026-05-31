using System;
using System.IO;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AICompanion
{
    public class ModEntry : Mod
    {
        // ── 消息类型常量 ──────────────────────────────────────────────
        private const string MSG_CHAT = "AIChat";

        private int tickCount = 0;
        private int _lastChatCount = 0;
        private ChatWindow _chatWindow;
        private IModHelper _helper;

        public override void Entry(IModHelper helper)
        {
            _helper = helper;
            
            var gameDir = Directory.GetParent(
                Directory.GetParent(helper.DirectoryPath).FullName
            ).FullName;

            GameConfig.Init(gameDir);

            Monitor.Log("=== AI Companion v0.9 ===", LogLevel.Info);
            Monitor.Log($"通信目录: {GameConfig.AIDir}", LogLevel.Info);

            // ── 初始化字幕窗口 ──────────────────────────────────────────
            _chatWindow = new ChatWindow(Monitor, helper);

            // ── 事件注册 ────────────────────────────────────────────────
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            // ★ 关键：在 Entry() 注册，主机和客户端都能收到广播
            helper.Events.Multiplayer.ModMessageReceived += OnMessageReceived;
        }

        // ── AI 聊天消息接收端（主机 & 客户端都注册）──────────────────
        private void OnMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != ModManifest.UniqueID) return;
            if (e.Type != MSG_CHAT) return;

            var msg = e.ReadAs<string>();

            // 获取发送者名字
            var aiPlayer = Game1.getFarmerMaybeOffline(e.FromPlayerID);
            string aiName = aiPlayer != null ? aiPlayer.Name : "AI";

            // 调试日志
            Monitor.Log($"[AI聊天] 主机收到: {msg}，chatWindow={_chatWindow != null}", LogLevel.Info);

            // 方案 A：直接写聊天框，不用 _chatWindow
            Game1.chatBox?.addMessage($"{msg}", Color.Cyan);
        }

        // ── 发送聊天消息（AI 实例调用）────────────────────────────────
        private void BroadcastChat(string text)
        {
            if (Context.IsMultiplayer)
            {
                // ★ 成功方案：SendMessage 广播
                Helper.Multiplayer.SendMessage(
                    message: text,
                    messageType: MSG_CHAT,
                    modIDs: new[] { ModManifest.UniqueID }
                );
                Monitor.Log($"[AI聊天] 广播: {text}", LogLevel.Info);
            }
            else
            {
                // 单人模式：直接显示
                _chatWindow?.ShowSubtitle($"AI: {text}");
                Monitor.Log($"[AI聊天] 单人显示: {text}", LogLevel.Info);
            }
        }

        // ── 主循环 ───────────────────────────────────────────────────────
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(30)) return;   // ≈ 0.5 秒

            tickCount++;

            // 1. 只有 AI 实例处理指令
            if (Context.IsMultiplayer && !Context.IsMainPlayer)
            {
                var instruction = InstructionExecutor.ReadInstruction(Monitor);
                if (instruction != null)
                {
                    // say 指令由 ModEntry 处理
                    if (instruction.Action?.ToLower() == "say")
                    {
                        if (!string.IsNullOrEmpty(instruction.Text))
                            BroadcastChat(instruction.Text);
                        
                        InstructionExecutor.ConfirmConsumed(Monitor);
                    }
                    else
                    {
                        // 其他指令正常执行
                        var result = InstructionExecutor.Execute(instruction, Monitor);
                        if (result.Success)
                            InstructionExecutor.ConfirmConsumed(Monitor);
                        else
                            Monitor.Log($"指令 [{instruction.Action}] 失败: {result.Error}", LogLevel.Warn);
                    }
                }
            }

            // 2. 每秒检测玩家聊天（两个实例都检测）
            if (tickCount % 2 == 0)
            {
                CheckChatMessages();
            }

            // 3. 每 30 秒打一次日志
            if (tickCount % 60 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                Monitor.Log(
                    $"[{state.TimeString}] {state.PlayerName} @ {state.LocationName} " +
                    $"({state.PlayerX:F0},{state.PlayerY:F0}) " +
                    $"HP:{state.Health}/{state.MaxHealth} E:{state.Energy}/{state.MaxEnergy}",
                    LogLevel.Info);
            }
        }

        // ── 检测玩家聊天消息 → 写入 chat.json ─────────────────────────
        private void CheckChatMessages()
        {
            try
            {
                var chatBox = Game1.chatBox;
                if (chatBox == null) return;

                var messagesField = chatBox.GetType().GetField("messages",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (messagesField?.GetValue(chatBox) is System.Collections.IList messages)
                {
                    int currentCount = messages.Count;
                    if (currentCount > _lastChatCount)
                    {
                        for (int i = _lastChatCount; i < currentCount; i++)
                        {
                            var msg = messages[i];
                            string messageText = ExtractChatText(msg);

                            if (!string.IsNullOrEmpty(messageText))
                            {
                                Monitor.Log($"[聊天] {messageText}", LogLevel.Info);
                                WriteChatJson("主人", messageText);
                            }
                        }
                        _lastChatCount = currentCount;
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[聊天] 检测失败: {ex.Message}", LogLevel.Debug);
            }
        }

        // ── 写入 chat.json ─────────────────────────────────────────────
        private void WriteChatJson(string sender, string message)
        {
            try
            {
                var chatFile = Path.Combine(GameConfig.AIDir, "chat.json");
                var chatData = new
                {
                    Sender = sender,
                    Message = message,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                var json = System.Text.Json.JsonSerializer.Serialize(chatData,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(chatFile, json);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[聊天] 写入失败: {ex.Message}", LogLevel.Warn);
            }
        }

        // ── 提取聊天文本 ─────────────────────────────────────────────
        private string ExtractChatText(object msg)
        {
            if (msg == null) return null;

            var msgField = msg.GetType().GetField("message",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (msgField != null)
            {
                var value = msgField.GetValue(msg);
                if (value is string text)
                    return text;
                if (value is System.Collections.IList snippets)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var snippet in snippets)
                    {
                        var snippetField = snippet.GetType().GetField("message",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (snippetField != null)
                            sb.Append(snippetField.GetValue(snippet));
                    }
                    return sb.ToString();
                }
            }

            var textProp = msg.GetType().GetProperty("text",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (textProp != null)
                return textProp.GetValue(msg)?.ToString();

            return null;
        }

        private void OnPlayerWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer) return;
            Monitor.Log($"进入地图: {e.NewLocation.Name}", LogLevel.Info);
        }

        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            var timeStr = $"{e.NewTime / 100:D2}:{e.NewTime % 100:D2}";
            Monitor.Log($"时间: {timeStr}", LogLevel.Info);
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            Monitor.Log("返回标题，清理通信文件", LogLevel.Info);
            try
            {
                if (File.Exists(GameConfig.StateFile))       File.Delete(GameConfig.StateFile);
                if (File.Exists(GameConfig.InstructionFile)) File.Delete(GameConfig.InstructionFile);
            }
            catch { }
        }
    }
}
