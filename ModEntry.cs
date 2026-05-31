using System;
using System.Diagnostics;
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

        // ── 视角切换 ──────────────────────────────────────────────────
        private bool _isFollowingAI = false;  // 是否正在跟随 AI 视角
        private Farmer _aiFarmer = null;      // AI 角色引用

        // ── 配置 ──────────────────────────────────────────────────────
        private ModConfig Config;
        private string _lastTestResult = "尚未测试";

        // ── Python 进程 ───────────────────────────────────────────────
        private Process _pythonProcess = null;

        public override void Entry(IModHelper helper)
        {
            _helper = helper;
            Config = helper.ReadConfig<ModConfig>();
            
            var gameDir = Directory.GetParent(
                Directory.GetParent(helper.DirectoryPath).FullName
            ).FullName;

            GameConfig.Init(gameDir);

            Monitor.Log("=== AI Companion v2.2 ===", LogLevel.Info);
            Monitor.Log($"通信目录: {GameConfig.AIDir}", LogLevel.Info);

            // ── 初始化字幕窗口 ──────────────────────────────────────────
            _chatWindow = new ChatWindow(Monitor, helper);

            // ── 事件注册 ────────────────────────────────────────────────
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Input.ButtonPressed += OnButtonPressed;

            // ★ 关键：在 Entry() 注册，主机和客户端都能收到广播
            helper.Events.Multiplayer.ModMessageReceived += OnMessageReceived;

            // ── 注册 GMCM 设置页面 ──────────────────────────────────────
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;

            // ── 自动启动 Python（仅主机） ────────────────────────────────
            if (Context.IsMainPlayer)
            {
                StartPythonBridge();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // GMCM 设置页面注册
        // ══════════════════════════════════════════════════════════════

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null)
            {
                Monitor.Log("[GMCM] 未找到 Generic Mod Config Menu，跳过设置页面注册", LogLevel.Warn);
                return;
            }

            // 注册配置
            configMenu.Register(
                mod: ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => Helper.WriteConfig(Config)
            );

            // ── AI 人设 ──────────────────────────────────────────────
            configMenu.AddSectionTitle(
                mod: ModManifest,
                text: () => "AI 人设设置"
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () => Config.AIName,
                setValue: value => Config.AIName = value,
                name: () => "AI 名字",
                tooltip: () => "给你的 AI 伙伴取个名字"
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () => Config.AIPersonality,
                setValue: value => Config.AIPersonality = value,
                name: () => "AI 性格",
                tooltip: () => "选择 AI 的性格类型",
                allowedValues: new[] { "活泼可爱", "温柔体贴", "搞笑幽默", "认真勤恳" }
            );

            // ── LLM 配置 ──────────────────────────────────────────────
            configMenu.AddSectionTitle(
                mod: ModManifest,
                text: () => "LLM API 配置"
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () => Config.LLMProvider,
                setValue: value =>
                {
                    Config.LLMProvider = value;
                    Config.ApplyPreset(value);
                },
                name: () => "API 提供商",
                tooltip: () => "选择 LLM 提供商，会自动填充 Base URL 和默认模型",
                allowedValues: new[] { "OpenAI", "Claude", "Gemini", "XAI", "DeepSeek", "MiMo", "MiMo Plan", "自定义" }
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () => Config.APIKey,
                setValue: value => Config.APIKey = value,
                name: () => "API Key",
                tooltip: () =>
                {
                    if (Config.LLMProvider == "MiMo")
                        return "MiMo Credits（按量付费）：platform.xiaomimimo.com 获取";
                    if (Config.LLMProvider == "MiMo Plan")
                        return "MiMo Token Plan（订阅制，按月/年）：platform.xiaomimimo.com 获取";
                    return "输入你的 API Key";
                }
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () =>
                {
                    // 如果 Model 已被用户手动覆盖，返回覆盖值
                    // 否则返回提供商的默认模型
                    if (ModConfig.LLMPresets.TryGetValue(Config.LLMProvider, out var preset))
                    {
                        // 如果当前 Model 不在提供商的可选列表中，重置为默认
                        if (!preset.Models.Contains(Config.Model))
                            Config.Model = preset.DefaultModel;
                    }
                    return Config.Model;
                },
                setValue: value => Config.Model = value,
                name: () => "模型",
                tooltip: () =>
                {
                    if (ModConfig.LLMPresets.TryGetValue(Config.LLMProvider, out var preset))
                        return $"可选: {string.Join(", ", preset.Models)}";
                    return "输入模型名称";
                },
                allowedValues: null
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                getValue: () =>
                {
                    if (ModConfig.LLMPresets.TryGetValue(Config.LLMProvider, out var preset))
                        return preset.BaseURL;
                    return Config.BaseURL;
                },
                setValue: value => { /* 自动根据提供商填充 */ },
                name: () => "Base URL",
                tooltip: () => "API 基础地址（自动根据提供商填充，建议不要修改）"
            );

            // ── 功能开关 ──────────────────────────────────────────────
            configMenu.AddSectionTitle(
                mod: ModManifest,
                text: () => "功能开关"
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                getValue: () => Config.AutoStartPython,
                setValue: value => Config.AutoStartPython = value,
                name: () => "自动启动 Python",
                tooltip: () => "启动游戏时自动运行 Python 控制脚本"
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                getValue: () => Config.EnableCompanionMode,
                setValue: value => Config.EnableCompanionMode = value,
                name: () => "陪伴模式",
                tooltip: () => "开启 AI 陪伴互动（主动打招呼、关心、闲聊）"
            );

            // ── API 测试 ──────────────────────────────────────────────
            configMenu.AddSectionTitle(
                mod: ModManifest,
                text: () => "API 测试"
            );

            configMenu.AddParagraph(
                mod: ModManifest,
                text: () => "点击开关测试 API 连接，结果会显示在下方和 SMAPI 控制台"
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                getValue: () => false,
                setValue: value =>
                {
                    if (value)
                    {
                        TestAPIConnection();
                    }
                },
                name: () => "▶ 测试 API 连接",
                tooltip: () => "点击测试当前 API 配置是否正确"
            );

            configMenu.AddParagraph(
                mod: ModManifest,
                text: () => _lastTestResult
            );

            Monitor.Log("[GMCM] 设置页面注册成功", LogLevel.Info);
        }

        // ══════════════════════════════════════════════════════════════
        // API 测试
        // ══════════════════════════════════════════════════════════════

        private void TestAPIConnection()
        {
            Monitor.Log("[API 测试] 开始测试...", LogLevel.Info);
            Monitor.Log($"[API 测试] 提供商: {Config.LLMProvider}", LogLevel.Info);
            Monitor.Log($"[API 测试] Base URL: {Config.BaseURL}", LogLevel.Info);
            Monitor.Log($"[API 测试] 模型: {Config.Model}", LogLevel.Info);

            if (string.IsNullOrEmpty(Config.APIKey))
            {
                _lastTestResult = "❌ API Key 为空！";
                Monitor.Log("[API 测试] ❌ API Key 为空！", LogLevel.Error);
                return;
            }

            Monitor.Log($"[API 测试] API Key: {Config.APIKey[..8]}...{Config.APIKey[^4..]}", LogLevel.Info);

            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                // 设置请求头
                if (Config.LLMProvider == "Claude")
                {
                    client.DefaultRequestHeaders.Add("x-api-key", Config.APIKey);
                    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                }
                else
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Config.APIKey}");
                }

                // 发送测试请求
                var requestBody = new
                {
                    model = Config.Model,
                    messages = new[]
                    {
                        new { role = "user", content = "Say 'API test successful' in 5 words or less." }
                    },
                    max_tokens = 50
                };

                var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                string endpoint = Config.LLMProvider == "Claude" ? "/messages" : "/chat/completions";
                var response = client.PostAsync($"{Config.BaseURL}{endpoint}", content).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = response.Content.ReadAsStringAsync().Result;
                    _lastTestResult = $"✅ 连接成功！模型: {Config.Model}";
                    Monitor.Log($"[API 测试] ✅ 连接成功！", LogLevel.Info);
                    Monitor.Log($"[API 测试] 响应: {responseBody[..Math.Min(200, responseBody.Length)]}...", LogLevel.Info);
                }
                else
                {
                    var errorBody = response.Content.ReadAsStringAsync().Result;
                    _lastTestResult = $"❌ 连接失败: {response.StatusCode}";
                    Monitor.Log($"[API 测试] ❌ 连接失败: {response.StatusCode}", LogLevel.Error);
                    Monitor.Log($"[API 测试] 错误: {errorBody[..Math.Min(200, errorBody.Length)]}", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                _lastTestResult = $"❌ 异常: {ex.Message}";
                Monitor.Log($"[API 测试] ❌ 异常: {ex.Message}", LogLevel.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // F5/F6 视角切换
        // ══════════════════════════════════════════════════════════════

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // F5 → 切换到 AI 视角
            if (e.Button == SButton.F5)
            {
                ToggleAIPerspective();
                _helper.Input.Suppress(e.Button);
            }

            // F6 → 切换回主人视角
            if (e.Button == SButton.F6)
            {
                ResetPerspective();
                _helper.Input.Suppress(e.Button);
            }
        }

        private void ToggleAIPerspective()
        {
            if (_isFollowingAI)
            {
                // 已经在跟随 → 取消
                ResetPerspective();
                return;
            }

            // 找到 AI 角色（非主机玩家）
            _aiFarmer = FindAIFarmer();
            if (_aiFarmer == null)
            {
                Monitor.Log("[视角] 找不到 AI 角色", LogLevel.Warn);
                Game1.chatBox?.addMessage("找不到 AI 角色", Color.Red);
                return;
            }

            _isFollowingAI = true;
            Monitor.Log($"[视角] 切换到 AI 视角: {_aiFarmer.Name}", LogLevel.Info);
            Game1.chatBox?.addMessage($"[视角] 跟随 AI: {_aiFarmer.Name} (按 F6 取消)", Color.Yellow);
        }

        private void ResetPerspective()
        {
            _isFollowingAI = false;
            _aiFarmer = null;
            Monitor.Log("[视角] 切换回主人视角", LogLevel.Info);
            Game1.chatBox?.addMessage("[视角] 已切回主人视角", Color.Yellow);
        }

        private Farmer FindAIFarmer()
        {
            // 找到非主机的玩家
            foreach (var farmer in Game1.getAllFarmers())
            {
                if (!farmer.IsMainPlayer)
                {
                    return farmer;
                }
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════
        // 自动启动 Python
        // ══════════════════════════════════════════════════════════════

        private void StartPythonBridge()
        {
            try
            {
                // 查找 Python 脚本
                string scriptPath = Path.Combine(GameConfig.AIDir, "..", "AICompanion", "ai_bridge.py");
                scriptPath = Path.GetFullPath(scriptPath);

                if (!File.Exists(scriptPath))
                {
                    Monitor.Log($"[Python] 找不到脚本: {scriptPath}", LogLevel.Warn);
                    Monitor.Log("[Python] 请手动运行 ai_bridge.py", LogLevel.Warn);
                    return;
                }

                // 查找 Python 解释器
                string pythonExe = FindPython();
                if (pythonExe == null)
                {
                    Monitor.Log("[Python] 找不到 Python 解释器", LogLevel.Warn);
                    Monitor.Log("[Python] 请手动运行 ai_bridge.py", LogLevel.Warn);
                    return;
                }

                // 启动 Python 进程
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-u \"{scriptPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(scriptPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                _pythonProcess = Process.Start(startInfo);
                Monitor.Log($"[Python] 已启动: {pythonExe} {scriptPath}", LogLevel.Info);
                Monitor.Log($"[Python] PID: {_pythonProcess.Id}", LogLevel.Info);

                // 读取输出（后台线程）
                _pythonProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Monitor.Log($"[Python] {e.Data}", LogLevel.Debug);
                };
                _pythonProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Monitor.Log($"[Python] {e.Data}", LogLevel.Warn);
                };
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Monitor.Log($"[Python] 启动失败: {ex.Message}", LogLevel.Warn);
            }
        }

        private string FindPython()
        {
            // 尝试常见的 Python 路径
            string[] candidates = {
                "python3",
                "python",
                "/usr/bin/python3",
                "/usr/local/bin/python3",
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                    });
                    process.WaitForExit(3000);
                    if (process.ExitCode == 0)
                        return candidate;
                }
                catch { }
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        // AI 聊天消息接收端
        // ══════════════════════════════════════════════════════════════

        private void OnMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != ModManifest.UniqueID) return;
            if (e.Type != MSG_CHAT) return;

            var msg = e.ReadAs<string>();

            // 获取发送者名字
            var aiPlayer = Game1.getFarmerMaybeOffline(e.FromPlayerID);
            string aiName = aiPlayer != null ? aiPlayer.Name : "AI";

            // 调试日志
            Monitor.Log($"[AI聊天] 主机收到: {msg}", LogLevel.Info);

            // 直接写聊天框
            Game1.chatBox?.addMessage($"{msg}", Color.Cyan);
        }

        // ── 发送聊天消息（AI 实例调用）────────────────────────────────
        private void BroadcastChat(string text)
        {
            if (Context.IsMultiplayer)
            {
                Helper.Multiplayer.SendMessage(
                    message: text,
                    messageType: MSG_CHAT,
                    modIDs: new[] { ModManifest.UniqueID }
                );
                Monitor.Log($"[AI聊天] 广播: {text}", LogLevel.Info);
            }
            else
            {
                _chatWindow?.ShowSubtitle($"AI: {text}");
                Monitor.Log($"[AI聊天] 单人显示: {text}", LogLevel.Info);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 主循环
        // ══════════════════════════════════════════════════════════════

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(30)) return;   // ≈ 0.5 秒

            tickCount++;

            // ── 1. F5 视角跟随 ──────────────────────────────────────────
            if (_isFollowingAI && _aiFarmer != null)
            {
                // 跟随 AI 角色
                Game1.viewport.X = (int)_aiFarmer.Position.X - Game1.viewport.Width / 2;
                Game1.viewport.Y = (int)_aiFarmer.Position.Y - Game1.viewport.Height / 2;
            }

            // ── 2. AI 实例处理指令 ──────────────────────────────────────
            if (Context.IsMultiplayer && !Context.IsMainPlayer)
            {
                // 写 state.json（供 Python 脚本读取）
                if (tickCount % 2 == 0)
                {
                    var state = GameStateReader.Read(Monitor);
                    GameStateReader.WriteState(state, Monitor);
                }

                // 处理指令
                var instruction = InstructionExecutor.ReadInstruction(Monitor);
                if (instruction != null)
                {
                    if (instruction.Action?.ToLower() == "say")
                    {
                        if (!string.IsNullOrEmpty(instruction.Text))
                            BroadcastChat(instruction.Text);
                        // ReadInstruction 内部已经原子地删除了文件，不需要再调 ConfirmConsumed
                    }
                    else
                    {
                        var result = InstructionExecutor.Execute(instruction, Monitor);
                        if (!result.Success)
                            Monitor.Log($"指令 [{instruction.Action}] 失败: {result.Error}", LogLevel.Warn);
                    }
                }
            }

            // ── 3. 每秒检测玩家聊天 ────────────────────────────────────
            if (tickCount % 2 == 0)
            {
                CheckChatMessages();
            }

            // ── 4. 每 30 秒打一次日志 ──────────────────────────────────
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

        // ══════════════════════════════════════════════════════════════
        // 聊天检测
        // ══════════════════════════════════════════════════════════════

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
                                Monitor.Log($"[调试] 准备写入 chat.json", LogLevel.Debug);
                                WriteChatJson("主人", messageText);
                                Monitor.Log($"[调试] chat.json 写入完成", LogLevel.Debug);
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

        // ══════════════════════════════════════════════════════════════
        // 其他事件
        // ══════════════════════════════════════════════════════════════

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
            
            // 停止 Python 进程
            StopPythonBridge();

            try
            {
                if (File.Exists(GameConfig.StateFile))       File.Delete(GameConfig.StateFile);
                if (File.Exists(GameConfig.InstructionFile)) File.Delete(GameConfig.InstructionFile);
            }
            catch { }
        }

        private void StopPythonBridge()
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                try
                {
                    _pythonProcess.Kill();
                    Monitor.Log("[Python] 已停止", LogLevel.Info);
                }
                catch { }
                _pythonProcess = null;
            }
        }
    }
}
