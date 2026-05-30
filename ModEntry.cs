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
        private int waitTicksRemaining = 0;  // wait 指令的剩余 tick 数
        private string lastInstructionHash = "";  // 去重：防止同一指令重复执行

        public override void Entry(IModHelper helper)
        {
            // 初始化通信路径（ai/ 目录在游戏根目录）
            // helper.DirectoryPath = Mods/AICompanion/，需要上两级到游戏根目录
            var gameDir = Directory.GetParent(Directory.GetParent(helper.DirectoryPath).FullName).FullName;
            GameConfig.Init(gameDir);

            Monitor.Log("=== AI Companion v0.2 ===", LogLevel.Info);
            Monitor.Log($"通信目录: {GameConfig.AIDir}", LogLevel.Info);
            Monitor.Log($"状态文件: {GameConfig.StateFile}", LogLevel.Info);
            Monitor.Log($"指令文件: {GameConfig.InstructionFile}", LogLevel.Info);

            // 清理旧的指令文件
            if (File.Exists(GameConfig.InstructionFile))
                File.Delete(GameConfig.InstructionFile);

            // 事件绑定
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Player.Warped += OnPlayerWarped;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(30)) return;  // 每 30 tick ≈ 0.5 秒

            tickCount++;

            // === 1. 处理 wait 指令 ===
            if (waitTicksRemaining > 0)
            {
                waitTicksRemaining--;
                return;  // 等待期间不处理新指令
            }

            // === 2. 读取并执行指令 ===
            var instruction = InstructionExecutor.ReadInstruction(Monitor);
            if (instruction != null)
            {
                // 去重：同样的指令不重复执行（walkTo 除外，允许重试）
                if (instruction.Action?.ToLower() != "walkto")
                {
                    var hash = $"{instruction.Action}:{instruction.X}:{instruction.Y}:{instruction.Slot}:{instruction.Npc}:{instruction.Text}";
                    if (hash == lastInstructionHash)
                    {
                        return;
                    }
                    lastInstructionHash = hash;
                }

                var result = InstructionExecutor.Execute(instruction, Monitor);

                if (result.Success)
                {
                    // 执行成功，删除指令文件
                    InstructionExecutor.ConfirmConsumed(Monitor);
                }
                else
                {
                    // 执行失败，保留文件等待重试
                    Monitor.Log($"指令 [{instruction.Action}] 失败: {result.Error}，保留文件等待重试", LogLevel.Warn);
                }

                // 特殊处理 wait 指令
                if (instruction.Action?.ToLower() == "wait" && result.Success)
                {
                    int ms = instruction.DurationMs ?? 1000;
                    waitTicksRemaining = ms / 500;  // 每 tick ≈ 500ms
                    if (waitTicksRemaining < 1) waitTicksRemaining = 1;
                }
            }

            // === 3. 每秒写一次状态 ===
            if (tickCount % 2 == 0)  // 每 2 tick ≈ 1 秒
            {
                var state = GameStateReader.Read(Monitor);
                GameStateReader.WriteState(state, Monitor);
            }

            // === 4. 每 30 秒打一次日志 ===
            if (tickCount % 60 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                Monitor.Log($"[{state.TimeString}] {state.PlayerName} @ {state.LocationName} " +
                    $"({state.PlayerX:F0},{state.PlayerY:F0}) " +
                    $"HP:{state.Health}/{state.MaxHealth} E:{state.Energy}/{state.MaxEnergy} " +
                    $"Gold:{state.Gold} Items:{state.ItemCount}",
                    LogLevel.Info);
            }
        }

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
                if (File.Exists(GameConfig.StateFile))
                    File.Delete(GameConfig.StateFile);
                if (File.Exists(GameConfig.InstructionFile))
                    File.Delete(GameConfig.InstructionFile);
            }
            catch { }
        }
    }
}
