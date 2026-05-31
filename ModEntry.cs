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
        // ── 消息类型常量，发送/接收两端保持一致 ──────────────────────────
        private const string MSG_CHAT = "AIChat";

        private int tickCount = 0;
        private int waitTicksRemaining = 0;
        private string lastInstructionHash = "";

        public override void Entry(IModHelper helper)
        {
            var gameDir = Directory.GetParent(
                Directory.GetParent(helper.DirectoryPath).FullName
            ).FullName;

            GameConfig.Init(gameDir);

            Monitor.Log("=== AI Companion v0.3 ===", LogLevel.Info);
            Monitor.Log($"通信目录: {GameConfig.AIDir}", LogLevel.Info);
            Monitor.Log($"状态文件: {GameConfig.StateFile}", LogLevel.Info);
            Monitor.Log($"指令文件: {GameConfig.InstructionFile}", LogLevel.Info);

            if (File.Exists(GameConfig.InstructionFile))
                File.Delete(GameConfig.InstructionFile);

            // ── 事件注册 ────────────────────────────────────────────────
            helper.Events.GameLoop.UpdateTicked    += OnUpdateTicked;
            helper.Events.Player.Warped            += OnPlayerWarped;
            helper.Events.GameLoop.TimeChanged     += OnTimeChanged;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            // ★ 关键修复：在 Entry() 注册，主机和客户端都能收到广播
            //   旧名 ModMessageReceived 已废弃，必须用 MessageReceived
            helper.Events.Multiplayer.ModMessageReceived += OnMessageReceived;
        }

        // ── AI 聊天消息接收端（主机 & 所有客户端都注册，都会触发）───────
        private void OnMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != ModManifest.UniqueID) return;
            if (e.Type != MSG_CHAT) return;

            var text = e.ReadAs<string>();
            Game1.chatBox?.addMessage(text, Color.White);
            Monitor.Log($"[AI聊天] 收到并显示: {text}", LogLevel.Info);
        }

        // ── 发送聊天消息（统一入口）──────────────────────────────────────
        private void BroadcastChat(string text)
        {
            if (Context.IsMultiplayer)
            {
                // SendMessage 会把消息发给所有已连接的玩家，
                // 主机自己也会触发 MessageReceived（SMAPI 会回环给自己）
                Helper.Multiplayer.SendMessage(
                    message:  text,
                    messageType: MSG_CHAT,
                    modIDs:   new[] { ModManifest.UniqueID }
                );
                Monitor.Log($"[AI聊天] 广播: {text}", LogLevel.Info);
            }
            else
            {
                // 单人模式：MessageReceived 不会触发，直接写本地
                Game1.chatBox?.addMessage(text, Color.White);
                Monitor.Log($"[AI聊天] 单人显示: {text}", LogLevel.Info);
            }
        }

        // ── 主循环 ───────────────────────────────────────────────────────
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            if (!e.IsMultipleOf(30)) return;   // ≈ 0.5 秒

            tickCount++;

            // 1. 处理 wait 指令
            if (waitTicksRemaining > 0)
            {
                waitTicksRemaining--;
                return;
            }

            // 2. 读取并执行指令
            var instruction = InstructionExecutor.ReadInstruction(Monitor);
            if (instruction != null)
            {
                // ★ 修复：say 指令排除在去重逻辑之外（允许连续说相同的话）
                bool skipDedup = instruction.Action?.ToLower() is "walkto" or "say";

                if (!skipDedup)
                {
                    var hash = $"{instruction.Action}:{instruction.X}:{instruction.Y}" +
                               $":{instruction.Slot}:{instruction.Npc}:{instruction.Text}";
                    if (hash == lastInstructionHash) return;
                    lastInstructionHash = hash;
                }

                // ★ say 指令由 ModEntry 统一处理（需要 Helper），不走 InstructionExecutor
                if (instruction.Action?.ToLower() == "say")
                {
                    if (!string.IsNullOrEmpty(instruction.Text))
                        BroadcastChat(instruction.Text);

                    InstructionExecutor.ConfirmConsumed(Monitor);
                    return;
                }

                var result = InstructionExecutor.Execute(instruction, Monitor);

                if (result.Success)
                {
                    InstructionExecutor.ConfirmConsumed(Monitor);
                }
                else
                {
                    Monitor.Log($"指令 [{instruction.Action}] 失败: {result.Error}，等待重试", LogLevel.Warn);
                }

                if (instruction.Action?.ToLower() == "wait" && result.Success)
                {
                    int ms = instruction.DurationMs ?? 1000;
                    waitTicksRemaining = Math.Max(1, ms / 500);
                }
            }

            // 3. 每秒写一次状态
            if (tickCount % 2 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                GameStateReader.WriteState(state, Monitor);
            }

            // 4. 每 30 秒打一次日志
            if (tickCount % 60 == 0)
            {
                var state = GameStateReader.Read(Monitor);
                Monitor.Log(
                    $"[{state.TimeString}] {state.PlayerName} @ {state.LocationName} " +
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
                if (File.Exists(GameConfig.StateFile))       File.Delete(GameConfig.StateFile);
                if (File.Exists(GameConfig.InstructionFile)) File.Delete(GameConfig.InstructionFile);
            }
            catch { }
        }
    }
}
