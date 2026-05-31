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

                var result = InstructionExecutor.Execute(instruction, Monitor);

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

            // === 3. 每 30 秒打一次日志 ===
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

        private void OnHostTick(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(60)) return;  // 每 1 秒

            tickCount++;

            // 每秒写一次状态（供 Python 监控）
            var state = GameStateReader.Read(Monitor);
            GameStateReader.WriteState(state, Monitor);
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
