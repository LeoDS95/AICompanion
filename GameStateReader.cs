using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;

namespace AICompanion
{
    /// <summary>
    /// 游戏状态快照
    /// </summary>
    public class GameStateSnapshot
    {
        // 玩家
        public string PlayerName { get; set; }
        public float PlayerX { get; set; }
        public float PlayerY { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Energy { get; set; }
        public int MaxEnergy { get; set; }
        public int Gold { get; set; }
        public int FarmingLevel { get; set; }
        public int MiningLevel { get; set; }
        public int FishingLevel { get; set; }
        public int CombatLevel { get; set; }

        // 世界
        public string LocationName { get; set; }
        public string Season { get; set; }
        public int Day { get; set; }
        public int Year { get; set; }
        public int TimeOfDay { get; set; }
        public string TimeString { get; set; }
        public string Weather { get; set; }

        // 周围
        public int NpcCount { get; set; }
        public int MonsterCount { get; set; }
        public int ItemCount { get; set; }

        // 背包摘要（前8格）
        public string InventorySummary { get; set; }

        // 多人联机
        public bool IsMultiplayer { get; set; }
        public bool IsMainPlayer { get; set; }
        public int PlayerCount { get; set; }

        // 通信状态
        public bool WaitingForInstruction { get; set; }
        public bool IsWalking { get; set; }          // 是否正在寻路行走中
        public int WalkStepsRemaining { get; set; }  // 剩余步数
        public string LastError { get; set; }
    }

    public static class GameStateReader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static GameStateSnapshot Read(IMonitor monitor)
        {
            var snapshot = new GameStateSnapshot();

            try
            {
                var player = Game1.player;
                var loc = Game1.currentLocation;

                // === 玩家信息 ===
                snapshot.PlayerName = player.Name ?? "无名";
                snapshot.PlayerX = player.Position.X;
                snapshot.PlayerY = player.Position.Y;
                snapshot.Health = player.health;
                snapshot.MaxHealth = player.maxHealth;
                snapshot.Energy = (int)player.Stamina;
                snapshot.MaxEnergy = (int)player.MaxStamina;
                snapshot.Gold = player.Money;

                snapshot.FarmingLevel = player.FarmingLevel;
                snapshot.MiningLevel = player.MiningLevel;
                snapshot.FishingLevel = player.FishingLevel;
                snapshot.CombatLevel = player.CombatLevel;

                // === 世界信息 ===
                snapshot.LocationName = loc?.Name ?? "未知";
                snapshot.Season = Game1.currentSeason ?? "未知";
                snapshot.Day = Game1.dayOfMonth;
                snapshot.Year = Game1.year;
                snapshot.TimeOfDay = Game1.timeOfDay;
                snapshot.TimeString = $"{Game1.timeOfDay / 100:D2}:{Game1.timeOfDay % 100:D2}";
                snapshot.Weather = GetWeather();

                // === 周围实体 ===
                if (loc != null)
                {
                    snapshot.NpcCount = loc.characters?.Count(c => c is NPC && !(c is Monster)) ?? 0;
                    snapshot.MonsterCount = loc.characters?.Count(c => c is Monster) ?? 0;
                }

                // === 背包 ===
                var items = player.Items?.Where(i => i != null).ToList();
                snapshot.ItemCount = items?.Count ?? 0;
                snapshot.InventorySummary = string.Join(", ",
                    items?.Take(8).Select(i => $"{i.Name}x{i.Stack}") ?? Enumerable.Empty<string>());

                // === 多人联机 ===
                snapshot.IsMultiplayer = Context.IsMultiplayer;
                snapshot.IsMainPlayer = Context.IsMainPlayer;
                snapshot.PlayerCount = Game1.otherFarmers?.Count + 1 ?? 1;

                // 寻路状态
                snapshot.IsWalking = player.controller != null;
                snapshot.WalkStepsRemaining = player.controller?.pathToEndPoint?.Count ?? 0;
                snapshot.WaitingForInstruction = !snapshot.IsWalking;
            }
            catch (Exception ex)
            {
                snapshot.LastError = ex.Message;
                monitor.Log($"读取游戏状态出错: {ex.Message}", LogLevel.Warn);
            }

            return snapshot;
        }

        /// <summary>
        /// 写入 state.json（原子操作：先写 .tmp 再 rename）
        /// </summary>
        public static void WriteState(GameStateSnapshot snapshot, IMonitor monitor)
        {
            try
            {
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                var tmpFile = GameConfig.StateFile + ".tmp";
                File.WriteAllText(tmpFile, json);
                File.Move(tmpFile, GameConfig.StateFile, overwrite: true);
            }
            catch (Exception ex)
            {
                monitor.Log($"写入 state.json 出错: {ex.Message}", LogLevel.Warn);
            }
        }

        private static string GetWeather()
        {
            if (Game1.isSnowing) return "下雪";
            if (Game1.isRaining) return "下雨";
            if (Game1.isLightning) return "雷暴";
            if (Game1.isDebrisWeather) return "大风";
            return "晴天";
        }
    }
}
