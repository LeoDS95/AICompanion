using System;
using System.IO;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AICompanion
{
    public class ModEntry : Mod
    {
        private int tickCount = 0;
        private int waitTicksRemaining = 0;
        private string lastInstructionHash = "";
        private IModHelper _helper;

        public override void Entry(IModHelper helper)
        {
            _helper = helper;

            // 初始化通信路径
            var gameDir = Directory.GetParent(Directory.GetParent(helper.DirectoryPath).FullName).FullName;
            GameConfig.Init(gameDir);

            Monitor.Log("=== AI Companion v0.5 ===", LogLevel.Info);
            Monitor.Log($"通信目录: {GameConfig.AIDir}", LogLevel.Info);

            // 等待游戏加载后再判断模式
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // 检查进程数，决定是否激活 AI
            int processCount = CountStardewProcesses();
            Monitor.Log($"[检测] 游戏进程数: {processCount}", LogLevel.Info);

            if (processCount <= 1)
            {
                // 只有一个进程 → 主机模式，不激活 AI
                Monitor.Log("[模式] 主机模式 - Mod 不激活", LogLevel.Info);
                return;
            }

            // 有多个进程 → 可能是 AI 实例，开始检测
            Monitor.Log("[模式] 检测到多个游戏实例，开始联机检测...", LogLevel.Info);
            _helper.Events.GameLoop.UpdateTicked += OnInitialCheck;
        }

        private int CountStardewProcesses()
        {
            try
            {
                // 统计 StardewModdingAPI.exe 进程数
                var processes = System.Diagnostics.Process.GetProcessesByName("StardewModdingAPI");
                return processes.Length;
            }
            catch
            {
                return 1;  // 出错时默认当作主机
            }
        }

        private int _initCheckTicks = 0;

        private void OnInitialCheck(object sender, UpdateTickedEventArgs e)
        {
            _initCheckTicks++;
            
            // 等待游戏世界加载完成
            if (!Context.IsWorldReady)
            {
                if (_initCheckTicks % 120 == 0)  // 每2秒打印一次
                    Monitor.Log("[检测] 等待游戏加载...", LogLevel.Info);
                return;
            }

            // 游戏已加载，检测模式
            _helper.Events.GameLoop.UpdateTicked -= OnInitialCheck;

            Monitor.Log($"[检测] 游戏已加载 IsMultiplayer={Context.IsMultiplayer}, IsMainPlayer={Context.IsMainPlayer}", LogLevel.Info);

            // 清理旧指令
            if (File.Exists(GameConfig.InstructionFile))
                File.Delete(GameConfig.InstructionFile);

            if (Context.IsMultiplayer && !Context.IsMainPlayer)
            {
                // === AI 模式：加入者 ===
                Monitor.Log("[模式] AI 实例 - 加入者模式", LogLevel.Info);
                Monitor.Log("[AI] 等待游戏加载完成后自动开始控制", LogLevel.Info);

                _helper.Events.GameLoop.UpdateTicked += OnAITick;
                _helper.Events.Player.Warped += OnPlayerWarped;
                _helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            }
            else
            {
                // === 主机模式：主人手动控制 ===
                Monitor.Log("[模式] 主机/单人模式 - 主人手动控制", LogLevel.Info);
                Monitor.Log("[主机] Mod 不干预，主人自由游戏", LogLevel.Info);

                // 主机不写状态文件，避免覆盖 AI 的状态
                _helper.Events.Player.Warped += OnPlayerWarped;
                _helper.Events.GameLoop.TimeChanged += OnTimeChanged;
                _helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
                _helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            }
        }

        // ==================== AI 模式 ====================
        // 读取状态 + 执行指令

        private void OnAITick(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(30)) return;  // 每 0.5 秒

            tickCount++;

            // 处理 wait 指令
            if (waitTicksRemaining > 0)
            {
                waitTicksRemaining--;
                return;
            }

            // === 1. 读取并执行指令 ===
            var instruction = InstructionExecutor.ReadInstruction(Monitor);
            if (instruction != null)
            {
                // 去重
                if (instruction.Action?.ToLower() != "walkto")
                {
                    var hash = $"{instruction.Action}:{instruction.X}:{instruction.Y}:{instruction.Slot}:{instruction.Npc}:{instruction.Text}";
                    if (hash == lastInstructionHash) return;
                    lastInstructionHash = hash;
                }

                var result = InstructionExecutor.Execute(instruction, Monitor, _helper);

                if (result.Success)
                {
                    InstructionExecutor.ConfirmConsumed(Monitor);
                    Monitor.Log($"[AI] 指令执行成功: {instruction.Action}", LogLevel.Info);
                }
                else
                {
                    Monitor.Log($"[AI] 指令失败: {instruction.Action} - {result.Error}", LogLevel.Warn);
                }

                if (instruction.Action?.ToLower() == "wait" && result.Success)
                {
                    int ms = instruction.DurationMs ?? 1000;
                    waitTicksRemaining = ms / 500;
                    if (waitTicksRemaining < 1) waitTicksRemaining = 1;
                }
            }

            // === 2. 每秒写一次状态 ===
            if (tickCount % 2 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                GameStateReader.WriteState(state, Monitor);
            }

            // === 3. 检查聊天消息 ===
            if (tickCount % 6 == 0)  // 每 0.5 秒检查一次
            {
                CheckChatMessages();
            }

            // === 4. 每 30 秒打一次日志 ===
            if (tickCount % 60 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                Monitor.Log($"[AI] [{state.TimeString}] {state.PlayerName} @ {state.LocationName} " +
                    $"({state.PlayerX:F0},{state.PlayerY:F0}) " +
                    $"HP:{state.Health} E:{state.Energy} Gold:{state.Gold}",
                    LogLevel.Info);
            }
        }

        // ==================== 主机模式 ====================
        // 只写状态，不执行指令（主人手动控制）

        private long _lastMessageTimestamp = 0;

        private void OnHostTick(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(60)) return;  // 每 1 秒

            tickCount++;

            // 每秒写一次状态（供 Python 监控）
            var state = GameStateReader.Read(Monitor);
            GameStateReader.WriteState(state, Monitor);

            // 检查 AI 消息
            CheckAIMessage();
        }

        private void CheckAIMessage()
        {
            try
            {
                var chatMsgFile = Path.Combine(GameConfig.AIDir, "ai_message.json");
                if (!File.Exists(chatMsgFile)) return;

                var json = File.ReadAllText(chatMsgFile);
                var msgData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                
                long timestamp = msgData.GetProperty("Timestamp").GetInt64();
                string text = msgData.GetProperty("Text").GetString();

                // 只处理新消息
                if (timestamp > _lastMessageTimestamp)
                {
                    _lastMessageTimestamp = timestamp;
                    if (!string.IsNullOrEmpty(text))
                    {
                        Game1.chatBox.addMessage(text, Microsoft.Xna.Framework.Color.Cyan);
                        Monitor.Log($"[主机] AI 说: {text}", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                // 检查消息失败不影响主功能
                Monitor.Log($"[主机] 检查 AI 消息失败: {ex.Message}", LogLevel.Debug);
            }
        }

        // ==================== 聊天检测 ====================

        private int _lastChatCount = 0;

        private void CheckChatMessages()
        {
            try
            {
                var chatBox = Game1.chatBox;
                if (chatBox == null) return;

                // 获取当前消息数量
                var messagesField = chatBox.GetType().GetField("messages",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (messagesField?.GetValue(chatBox) is System.Collections.IList messages)
                {
                    int currentCount = messages.Count;
                    if (currentCount > _lastChatCount)
                    {
                        // 有新消息
                        for (int i = _lastChatCount; i < currentCount; i++)
                        {
                            var msg = messages[i];
                            
                            // 提取消息文本（可能是 ChatSnippet 列表）
                            string messageText = ExtractChatText(msg);

                            if (!string.IsNullOrEmpty(messageText) && !messageText.StartsWith("[AI]"))
                            {
                                Monitor.Log($"[聊天] {messageText}", LogLevel.Info);

                                // 写入聊天文件供 Python 读取
                                WriteChatMessage("主人", messageText);
                            }
                        }
                        _lastChatCount = currentCount;
                    }
                }
            }
            catch (Exception ex)
            {
                // 聊天检测失败不影响主功能
                Monitor.Log($"[聊天] 检测失败: {ex.Message}", LogLevel.Debug);
            }
        }

        private void WriteChatMessage(string sender, string message)
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

        /// <summary>
        /// 从聊天消息对象中提取文本
        /// 消息可能是 string 或 List&lt;ChatSnippet&gt;
        /// </summary>
        private string ExtractChatText(object msg)
        {
            if (msg == null) return null;

            // 如果是字符串，直接返回
            if (msg is string str) return str;

            try
            {
                // 尝试获取 message 字段
                var msgField = msg.GetType().GetField("message",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (msgField != null)
                {
                    var value = msgField.GetValue(msg);
                    
                    // 如果是字符串，直接返回
                    if (value is string text) return text;
                    
                    // 如果是 List<Snippet>，遍历拼接
                    if (value is System.Collections.IList snippets)
                    {
                        var result = new System.Text.StringBuilder();
                        foreach (var snippet in snippets)
                        {
                            // 每个 Snippet 可能有 message 或 text 字段
                            var snippetField = snippet.GetType().GetField("message",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) ??
                                snippet.GetType().GetField("text",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            
                            if (snippetField != null)
                            {
                                result.Append(snippetField.GetValue(snippet));
                            }
                        }
                        return result.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[聊天] 提取文本失败: {ex.Message}", LogLevel.Debug);
            }

            return msg.ToString();
        }

        // ==================== 主机消息接收 ====================

        private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            try
            {
                // 只处理来自同一 Mod 的消息
                if (e.FromModID != ModManifest.UniqueID) return;
                if (e.Type != "AIChat") return;

                // 读取消息内容
                string message = e.ReadAs<string>();
                if (string.IsNullOrEmpty(message)) return;

                Monitor.Log($"[主机] 收到 AI 消息: {message}", LogLevel.Info);

                // 显示在主机的聊天框
                Game1.chatBox.addMessage(message, Microsoft.Xna.Framework.Color.Cyan);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[主机] 处理消息失败: {ex.Message}", LogLevel.Warn);
            }
        }

        // ==================== 通用事件 ====================

        private void OnPlayerWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer) return;
            Monitor.Log($"进入地图: {e.NewLocation.Name}", LogLevel.Info);

            // 地图切换时立即更新状态
            var state = GameStateReader.Read(Monitor);
            GameStateReader.WriteState(state, Monitor);
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
                if (File.Exists(GameConfig.StateFile)) File.Delete(GameConfig.StateFile);
                if (File.Exists(GameConfig.InstructionFile)) File.Delete(GameConfig.InstructionFile);
            }
            catch { }
        }
    }
}
