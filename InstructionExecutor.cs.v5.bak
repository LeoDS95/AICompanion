using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace AICompanion
{
    /// <summary>指令结构</summary>
    public class Instruction
    {
        public string Action      { get; set; } // moveTo, walkTo, interact, useItem, changeItem, talkTo, emote, say, wait
        public int?   X           { get; set; }
        public int?   Y           { get; set; }
        public int?   Slot        { get; set; }
        public string Npc         { get; set; }
        public string Text        { get; set; }
        public int?   DurationMs  { get; set; }
    }

    /// <summary>指令执行结果</summary>
    public class InstructionResult
    {
        public bool   Success { get; set; }
        public string Error   { get; set; }
        public string Action  { get; set; }
    }

    public static class InstructionExecutor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // ── 读取指令 ─────────────────────────────────────────────────────
        public static Instruction ReadInstruction(IMonitor monitor)
        {
            try
            {
                if (!File.Exists(GameConfig.InstructionFile)) return null;

                var json = File.ReadAllText(GameConfig.InstructionFile);
                File.WriteAllText(GameConfig.InstructionFile, ""); // 立即清空，防止重复读

                if (string.IsNullOrWhiteSpace(json)) return null;

                var instruction = JsonSerializer.Deserialize<Instruction>(json, JsonOptions);
                try { File.Delete(GameConfig.InstructionFile); } catch { }
                return instruction;
            }
            catch (Exception ex)
            {
                monitor.Log($"读取指令出错: {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        /// <summary>
        /// 确认指令已消费（删除指令文件）
        /// </summary>
        public static void ConfirmConsumed(IMonitor monitor)
        {
            try
            {
                if (File.Exists(GameConfig.InstructionFile))
                    File.Delete(GameConfig.InstructionFile);
            }
            catch (Exception ex)
            {
                monitor.Log($"删除指令文件失败: {ex.Message}", LogLevel.Warn);
            }
        }

        // ── 执行分发 ─────────────────────────────────────────────────────
        public static InstructionResult Execute(Instruction instruction, IMonitor monitor)
        {
            if (instruction == null || string.IsNullOrEmpty(instruction.Action))
                return new InstructionResult { Success = false, Error = "空指令" };

            try
            {
                return instruction.Action.ToLower() switch
                {
                    "moveto"     => ExecuteMoveTo(instruction, monitor),
                    "walkto"     => ExecuteWalkTo(instruction, monitor),
                    "interact"   => ExecuteInteract(instruction, monitor),
                    "useitem"    => ExecuteUseItem(instruction, monitor),
                    "changeitem" => ExecuteChangeItem(instruction, monitor),
                    "talkto"     => ExecuteTalkTo(instruction, monitor),
                    "emote"      => ExecuteEmote(instruction, monitor),
                    "wait"       => ExecuteWait(instruction, monitor),

                    // ★ 高层意图（Python 决定做什么，Mod 决定怎么做）
                    "goal"       => ExecuteGoal(instruction, monitor),

                    // ★ say 由 ModEntry.BroadcastChat() 处理，不应到达这里
                    "say"        => new InstructionResult
                    {
                        Success = false,
                        Action  = "say",
                        Error   = "say 应由 ModEntry 拦截，未到达 Executor"
                    },

                    _ => new InstructionResult
                    {
                        Success = false,
                        Action  = instruction.Action,
                        Error   = $"未知指令: {instruction.Action}"
                    }
                };
            }
            catch (Exception ex)
            {
                monitor.Log($"执行指令 [{instruction.Action}] 出错: {ex.Message}", LogLevel.Warn);
                return new InstructionResult
                {
                    Success = false,
                    Action  = instruction.Action,
                    Error   = ex.Message
                };
            }
        }

        // ── moveTo：直接传送到指定 tile ─────────────────────────────────
        private static InstructionResult ExecuteMoveTo(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "moveTo", Error = "缺少 X 或 Y" };

            var player = Game1.player;
            float px = inst.X.Value * 64f;
            float py = inst.Y.Value * 64f;
            player.Position = new Vector2(px, py);

            monitor.Log($"moveTo tile ({inst.X},{inst.Y}) → 像素 ({px},{py})", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "moveTo" };
        }

        // ── walkTo：A* 寻路，有动画 ──────────────────────────────────────
        private static InstructionResult ExecuteWalkTo(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "walkTo", Error = "缺少 X 或 Y" };

            var player = Game1.player;
            var loc    = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "walkTo", Error = "当前无位置" };

            int curX = (int)(player.Position.X / 64f);
            int curY = (int)(player.Position.Y / 64f);
            int tgtX = inst.X.Value;
            int tgtY = inst.Y.Value;

            var path = FindPath(curX, curY, tgtX, tgtY, loc, monitor);
            if (path == null || path.Count == 0)
            {
                monitor.Log($"walkTo ({tgtX},{tgtY}) 寻路失败，调试：", LogLevel.Warn);
                LogSurroundingTiles(tgtX, tgtY, loc, "目标", monitor);
                LogSurroundingTiles(curX, curY, loc, "起点", monitor);
                return new InstructionResult { Success = false, Action = "walkTo", Error = "无可达路径" };
            }

            player.controller = new PathFindController(path, player, loc)
            {
                nonDestructivePathing = true
            };
            monitor.Log($"walkTo ({tgtX},{tgtY})：开始行走，路径 {path.Count} 步", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "walkTo" };
        }

        private static void LogSurroundingTiles(int cx, int cy, GameLocation loc, string label, IMonitor monitor)
        {
            int w = loc.Map?.Layers?[0]?.LayerWidth  ?? 50;
            int h = loc.Map?.Layers?[0]?.LayerHeight ?? 50;
            monitor.Log($"{label} ({cx},{cy}) 周围：", LogLevel.Warn);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int tx = cx + dx, ty = cy + dy;
                monitor.Log($"  tile ({tx},{ty}): {(IsWalkable(tx,ty,loc,w,h) ? "可走" : "不可走")}", LogLevel.Warn);
            }
        }

        // ── A* ───────────────────────────────────────────────────────────
        private static Stack<Point> FindPath(int sx, int sy, int ex, int ey, GameLocation loc, IMonitor monitor)
        {
            int mapW = loc.Map?.Layers?[0]?.LayerWidth  ?? 50;
            int mapH = loc.Map?.Layers?[0]?.LayerHeight ?? 50;

            if (ex < 0 || ey < 0 || ex >= mapW || ey >= mapH) return null;
            if (!IsWalkable(ex, ey, loc, mapW, mapH)) return null;

            var openSet   = new SortedSet<(int f, int g, int x, int y)>();
            var cameFrom  = new Dictionary<(int,int),(int,int)>();
            var gScore    = new Dictionary<(int,int),int>();
            var closed    = new HashSet<(int,int)>();

            gScore[(sx,sy)] = 0;
            openSet.Add((H(sx,sy,ex,ey), 0, sx, sy));

            monitor.Log(
                $"A* ({sx},{sy})→({ex},{ey})  " +
                $"起点可走={IsWalkable(sx,sy,loc,mapW,mapH)} 终点可走={IsWalkable(ex,ey,loc,mapW,mapH)}",
                LogLevel.Info);

            int iter = 0;
            while (openSet.Count > 0 && iter++ < 1000)
            {
                var cur = openSet.Min;
                openSet.Remove(cur);
                var pos = (cur.x, cur.y);

                if (pos == (ex, ey))
                {
                    var path = new Stack<Point>();
                    var node = pos;
                    while (cameFrom.ContainsKey(node))
                    {
                        path.Push(new Point(node.Item1, node.Item2));
                        node = cameFrom[node];
                    }
                    monitor.Log($"A* 找到路径 {path.Count} 步", LogLevel.Info);
                    return path;
                }

                closed.Add(pos);
                int[][] dirs = { new[]{0,-1}, new[]{0,1}, new[]{-1,0}, new[]{1,0} };
                foreach (var d in dirs)
                {
                    int nx = cur.x + d[0], ny = cur.y + d[1];
                    var nb = (nx, ny);
                    if (closed.Contains(nb) || !IsWalkable(nx, ny, loc, mapW, mapH)) continue;

                    int tg = gScore[pos] + 1;
                    if (!gScore.ContainsKey(nb) || tg < gScore[nb])
                    {
                        cameFrom[nb] = pos;
                        gScore[nb]   = tg;
                        openSet.Add((tg + H(nx,ny,ex,ey), tg, nx, ny));
                    }
                }
            }

            monitor.Log($"A* 探索 {iter} 个 tile，无路径", LogLevel.Warn);
            return null;
        }

        private static int H(int x1, int y1, int x2, int y2)
            => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

        private static bool IsWalkable(int x, int y, GameLocation loc, int mapW, int mapH)
        {
            if (x < 0 || y < 0 || x >= mapW || y >= mapH) return false;

            // 玩家当前 tile 始终可通行（允许从障碍物位置出发）
            int px = (int)(Game1.player.Position.X / 64f);
            int py = (int)(Game1.player.Position.Y / 64f);
            if (x == px && y == py) return true;

            // ✅ 用 isTilePassable 检查 tile 属性（不检查家具碰撞体）
            return loc.isTilePassable(
                new xTile.Dimensions.Location(x, y),
                Game1.viewport
            );
        }

        // ── goal（高层意图执行）────────────────────────────────────────────
        private static InstructionResult ExecuteGoal(Instruction inst, IMonitor monitor)
        {
            var goal = inst.Text?.ToLower();
            if (string.IsNullOrEmpty(goal))
                return new InstructionResult { Success = false, Action = "goal", Error = "缺少目标" };

            monitor.Log($"[目标] 执行: {goal}", LogLevel.Info);

            var player = Game1.player;
            var location = player.currentLocation;
            var locationName = location.Name;

            return goal switch
            {
                "go_outside" => ExecuteGoOutside(player, location, locationName, monitor),
                "go_home" => ExecuteGoHome(player, location, locationName, monitor),
                "go_to_crops" => ExecuteGoToCrops(player, location, locationName, monitor),
                "go_to_town" => ExecuteGoToTown(player, location, locationName, monitor),
                "rest" => ExecuteRest(player, monitor),
                "wander" => ExecuteWander(player, location, monitor),
                "stay_inside" => new InstructionResult { Success = true, Action = "goal", Error = "待在室内" },
                _ => new InstructionResult { Success = false, Action = "goal", Error = $"未知目标: {goal}" }
            };
        }

        private static InstructionResult ExecuteGoOutside(Farmer player, GameLocation location, string locationName, IMonitor monitor)
        {
            // 如果在农舍/小屋，找到门并走出去
            if (locationName.Contains("House") || locationName.Contains("Cabin"))
            {
                // 找到出口（通常是 warp 点）
                foreach (var warp in location.warps)
                {
                    if (warp.TargetName.Contains("Farm") || warp.TargetName.Contains("Outside"))
                    {
                        // 走到出口
                        var path = FindPath(
                            (int)(player.Position.X / 64f),
                            (int)(player.Position.Y / 64f),
                            warp.X, warp.Y,
                            location, monitor
                        );

                        if (path != null)
                        {
                            player.controller = new PathFindController(path, player, location);
                            player.controller.endBehaviorFunction = (who, loc) =>
                            {
                                // 到达出口后触发换图
                                Game1.warpFarmer(warp.TargetName, warp.TargetX, warp.TargetY, false);
                            };
                            return new InstructionResult { Success = true, Action = "goal" };
                        }
                    }
                }
                return new InstructionResult { Success = false, Action = "goal", Error = "找不到出口" };
            }

            // 已经在室外
            return new InstructionResult { Success = true, Action = "goal" };
        }

        private static InstructionResult ExecuteGoHome(Farmer player, GameLocation location, string locationName, IMonitor monitor)
        {
            // 如果已经在农舍/小屋
            if (locationName.Contains("House") || locationName.Contains("Cabin"))
                return new InstructionResult { Success = true, Action = "goal" };

            // 找到农舍的 warp 点
            foreach (var warp in location.warps)
            {
                if (warp.TargetName.Contains("House") || warp.TargetName.Contains("Cabin"))
                {
                    var path = FindPath(
                        (int)(player.Position.X / 64f),
                        (int)(player.Position.Y / 64f),
                        warp.X, warp.Y,
                        location, monitor
                    );

                    if (path != null)
                    {
                        player.controller = new PathFindController(path, player, location);
                        player.controller.endBehaviorFunction = (who, loc) =>
                        {
                            Game1.warpFarmer(warp.TargetName, warp.TargetX, warp.TargetY, false);
                        };
                        return new InstructionResult { Success = true, Action = "goal" };
                    }
                }
            }

            return new InstructionResult { Success = false, Action = "goal", Error = "找不到回家的路" };
        }

        private static InstructionResult ExecuteGoToCrops(Farmer player, GameLocation location, string locationName, IMonitor monitor)
        {
            // 如果在农舍，先出门
            if (locationName.Contains("House") || locationName.Contains("Cabin"))
            {
                var result = ExecuteGoOutside(player, location, locationName, monitor);
                if (!result.Success) return result;
            }

            // 走到作物区（农场中心附近）
            var path = FindPath(
                (int)(player.Position.X / 64f),
                (int)(player.Position.Y / 64f),
                40, 30,  // 作物区中心
                player.currentLocation, monitor
            );

            if (path != null)
            {
                player.controller = new PathFindController(path, player, player.currentLocation);
                return new InstructionResult { Success = true, Action = "goal" };
            }

            return new InstructionResult { Success = false, Action = "goal", Error = "找不到作物区" };
        }

        private static InstructionResult ExecuteGoToTown(Farmer player, GameLocation location, string locationName, IMonitor monitor)
        {
            // 如果在农舍，先出门
            if (locationName.Contains("House") || locationName.Contains("Cabin"))
            {
                var result = ExecuteGoOutside(player, location, locationName, monitor);
                if (!result.Success) return result;
            }

            // 找到去镇上的 warp
            foreach (var warp in player.currentLocation.warps)
            {
                if (warp.TargetName.Contains("Town"))
                {
                    var path = FindPath(
                        (int)(player.Position.X / 64f),
                        (int)(player.Position.Y / 64f),
                        warp.X, warp.Y,
                        player.currentLocation, monitor
                    );

                    if (path != null)
                    {
                        player.controller = new PathFindController(path, player, player.currentLocation);
                        player.controller.endBehaviorFunction = (who, loc) =>
                        {
                            Game1.warpFarmer(warp.TargetName, warp.TargetX, warp.TargetY, false);
                        };
                        return new InstructionResult { Success = true, Action = "goal" };
                    }
                }
            }

            return new InstructionResult { Success = false, Action = "goal", Error = "找不到去镇上的路" };
        }

        private static InstructionResult ExecuteRest(Farmer player, IMonitor monitor)
        {
            // 等待一段时间恢复体力
            return new InstructionResult { Success = true, Action = "goal" };
        }

        private static InstructionResult ExecuteWander(Farmer player, GameLocation location, IMonitor monitor)
        {
            // 随机走动
            var random = new Random();
            int targetX = random.Next(10, location.Map.Layers[0].LayerWidth - 10);
            int targetY = random.Next(10, location.Map.Layers[0].LayerHeight - 10);

            var path = FindPath(
                (int)(player.Position.X / 64f),
                (int)(player.Position.Y / 64f),
                targetX, targetY,
                location, monitor
            );

            if (path != null)
            {
                player.controller = new PathFindController(path, player, location);
                return new InstructionResult { Success = true, Action = "goal" };
            }

            return new InstructionResult { Success = false, Action = "goal", Error = "找不到闲逛目标" };
        }

        // ── interact ─────────────────────────────────────────────────────
        private static InstructionResult ExecuteInteract(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "interact", Error = "缺少 X 或 Y" };

            var loc = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "interact", Error = "当前无位置" };

            bool ok = loc.checkAction(
                new xTile.Dimensions.Location(inst.X.Value, inst.Y.Value),
                Game1.viewport,
                Game1.player);

            monitor.Log($"interact tile ({inst.X},{inst.Y}): {(ok ? "成功" : "无响应")}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "interact" };
        }

        // ── useItem ──────────────────────────────────────────────────────
        private static InstructionResult ExecuteUseItem(Instruction inst, IMonitor monitor)
        {
            if (inst.Slot == null)
                return new InstructionResult { Success = false, Action = "useItem", Error = "缺少 Slot" };

            var player = Game1.player;
            if (inst.Slot.Value < 0 || inst.Slot.Value >= player.Items.Count)
                return new InstructionResult { Success = false, Action = "useItem", Error = $"槽位 {inst.Slot} 超出范围" };

            var item = player.Items[inst.Slot.Value];
            if (item == null)
                return new InstructionResult { Success = false, Action = "useItem", Error = $"槽位 {inst.Slot} 为空" };

            player.CurrentToolIndex = inst.Slot.Value;
            player.CurrentTool?.beginUsing(Game1.currentLocation,
                (int)player.Position.X, (int)player.Position.Y, player);

            monitor.Log($"useItem 槽 {inst.Slot}: {item.Name}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "useItem" };
        }

        // ── changeItem ───────────────────────────────────────────────────
        private static InstructionResult ExecuteChangeItem(Instruction inst, IMonitor monitor)
        {
            if (inst.Slot == null)
                return new InstructionResult { Success = false, Action = "changeItem", Error = "缺少 Slot" };

            var player = Game1.player;
            if (inst.Slot.Value < 0 || inst.Slot.Value >= player.Items.Count)
                return new InstructionResult { Success = false, Action = "changeItem", Error = $"槽位 {inst.Slot} 超出范围" };

            player.CurrentToolIndex = inst.Slot.Value;
            monitor.Log($"changeItem → 槽 {inst.Slot}: {player.Items[inst.Slot.Value]?.Name ?? "空"}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "changeItem" };
        }

        // ── talkTo ───────────────────────────────────────────────────────
        private static InstructionResult ExecuteTalkTo(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Npc))
                return new InstructionResult { Success = false, Action = "talkTo", Error = "缺少 NPC 名字" };

            var loc = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "talkTo", Error = "当前无位置" };

            NPC target = null;
            foreach (var ch in loc.characters)
            {
                if (ch is NPC npc && npc.Name.Equals(inst.Npc, StringComparison.OrdinalIgnoreCase))
                { target = npc; break; }
            }

            if (target == null)
                return new InstructionResult { Success = false, Action = "talkTo", Error = $"NPC '{inst.Npc}' 不在当前地图" };

            target.checkAction(Game1.player, Game1.currentLocation);
            monitor.Log($"talkTo {inst.Npc}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "talkTo" };
        }

        // ── emote ────────────────────────────────────────────────────────
        private static InstructionResult ExecuteEmote(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Text))
                return new InstructionResult { Success = false, Action = "emote", Error = "缺少表情" };

            var emoteMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["happy"]    = 4,  ["sad"]      = 24, ["angry"]    = 28,
                ["love"]     = 20, ["surprise"] = 16, ["sleep"]    = 5,
                ["note"]     = 12, ["question"] = 40, ["shock"]    = 16
            };

            if (int.TryParse(inst.Text, out int id) ||
                emoteMap.TryGetValue(inst.Text, out id))
            {
                Game1.player.doEmote(id);
                monitor.Log($"emote: {inst.Text} ({id})", LogLevel.Info);
                return new InstructionResult { Success = true, Action = "emote" };
            }

            return new InstructionResult { Success = false, Action = "emote", Error = $"未知表情: {inst.Text}" };
        }

        // ── wait ─────────────────────────────────────────────────────────
        private static InstructionResult ExecuteWait(Instruction inst, IMonitor monitor)
        {
            monitor.Log($"wait {inst.DurationMs ?? 1000}ms", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "wait" };
        }
    }
}
