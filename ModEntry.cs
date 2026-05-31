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

        public override void Entry(IModHelper helper)
        {
            var gameDir = Directory.GetParent(Directory.GetParent(helper.DirectoryPath).FullName).FullName;
            GameConfig.Init(gameDir);

            Monitor.Log("=== AI Companion v0.3 ===", LogLevel.Info);
            Monitor.Log($"通信目录: {GameConfig.AIDir}", LogLevel.Info);
            Monitor.Log($"状态文件: {GameConfig.StateFile}", LogLevel.Info);
            Monitor.Log($"指令文件: {GameConfig.InstructionFile}", LogLevel.Info);

            // 清理旧的指令文件
            if (File.Exists(GameConfig.InstructionFile))
                File.Delete(GameConfig.InstructionFile);

            helper.Events.GameLoop.UpdateTicked   += OnUpdateTicked;
            helper.Events.Player.Warped           += OnPlayerWarped;
            helper.Events.GameLoop.TimeChanged    += OnTimeChanged;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(30)) return; // 每 30 tick ≈ 0.5 秒

            tickCount++;

            // === 1. 处理 wait 指令 ===
            if (waitTicksRemaining > 0)
            {
                waitTicksRemaining--;
                return;
            }

            // === 2. 读取并执行指令 ===
            // ReadInstruction 内部已经原子地删除了文件，这里不需要再调 ConfirmConsumed
            var instruction = InstructionExecutor.ReadInstruction(Monitor);
            if (instruction != null)
            {
                // 去重（walkTo 允许重试，不去重）
                if (instruction.Action?.ToLower() != "walkto")
                {
                    var hash = $"{instruction.Action}:{instruction.X}:{instruction.Y}" +
                               $":{instruction.Slot}:{instruction.Npc}:{instruction.Text}";
                    if (hash == lastInstructionHash)
                        return;
                    lastInstructionHash = hash;
                }

                var result = InstructionExecutor.Execute(instruction, Monitor);

                if (!result.Success)
                {
                    Monitor.Log($"指令 [{instruction.Action}] 失败: {result.Error}，保留等待重试", LogLevel.Warn);
                    // 失败时把指令文件写回去，让 Python 端知道还没消费
                    // （这里选择不写回，由冷却机制自然重试——简化处理）
                }

                // 特殊处理 wait
                if (instruction.Action?.ToLower() == "wait" && result.Success)
                {
                    int ms = instruction.DurationMs ?? 1000;
                    waitTicksRemaining = Math.Max(1, ms / 500);
                }
            }

            // === 3. 每秒写一次状态 ===
            if (tickCount % 2 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                GameStateReader.WriteState(state, Monitor);
            }

            // === 4. 每 30 秒打一次日志 ===
            if (tickCount % 60 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                Monitor.Log(
                    $"[{state.TimeString}] {state.PlayerName} @ {state.LocationName} " +
                    $"({state.PlayerX / 64:F0},{state.PlayerY / 64:F0}) " +
                    $"HP:{state.Health}/{state.MaxHealth} E:{state.Energy}/{state.MaxEnergy} " +
                    $"Gold:{state.Gold} Items:{state.ItemCount}",
                    LogLevel.Info);
            }
        }

        private void OnPlayerWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer) return;
            Monitor.Log($"进入地图: {e.NewLocation.Name}", LogLevel.Info);
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
            try { if (File.Exists(GameConfig.StateFile))       File.Delete(GameConfig.StateFile); }       catch { }
            try { if (File.Exists(GameConfig.InstructionFile)) File.Delete(GameConfig.InstructionFile); } catch { }
        }
    }
}
