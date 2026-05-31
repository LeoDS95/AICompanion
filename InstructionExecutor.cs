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
        public string Action     { get; set; } // walkTo, moveTo, interact, useItem, changeItem, talkTo, emote, say, wait
        public int?   X          { get; set; }
        public int?   Y          { get; set; }
        public int?   Slot       { get; set; }
        public string Npc        { get; set; }
        public string Text       { get; set; }
        public int?   DurationMs { get; set; }
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

        // =====================================================================
        // 读取指令
        // =====================================================================

        /// <summary>
        /// 读取 instruction.json。
        /// 内部原子地完成：读取 → 立即删除文件（防止重复执行）。
        /// 没有文件或解析失败返回 null。
        /// </summary>
        public static Instruction ReadInstruction(IMonitor monitor)
        {
            if (!File.Exists(GameConfig.InstructionFile))
                return null;

            string json = null;
            try
            {
                // 读完后立刻删除，避免 Python 端看到"旧指令还在"
                json = File.ReadAllText(GameConfig.InstructionFile);
                File.Delete(GameConfig.InstructionFile);
            }
            catch (Exception ex)
            {
                monitor.Log($"读取/删除指令文件出错: {ex.Message}", LogLevel.Warn);
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<Instruction>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                monitor.Log($"解析指令 JSON 出错: {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        // =====================================================================
        // 执行指令
        // =====================================================================

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
                    "say"        => ExecuteSay(instruction, monitor),
                    "wait"       => ExecuteWait(instruction, monitor),
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

        // =====================================================================
        // moveTo — 直接传送（无动画）
        // =====================================================================

        private static InstructionResult ExecuteMoveTo(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "moveTo", Error = "缺少 X 或 Y" };

            var player  = Game1.player;
            float px    = inst.X.Value * 64f;
            float py    = inst.Y.Value * 64f;
            player.Position = new Vector2(px, py);

            monitor.Log($"moveTo tile ({inst.X},{inst.Y})", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "moveTo" };
        }

        // =====================================================================
        // walkTo — A* 寻路行走（有动画）
        // =====================================================================

        private static InstructionResult ExecuteWalkTo(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "walkTo", Error = "缺少 X 或 Y" };

            var player = Game1.player;
            var loc    = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "walkTo", Error = "当前无位置" };

            int curX    = (int)(player.Position.X / 64f);
            int curY    = (int)(player.Position.Y / 64f);
            int targetX = inst.X.Value;
            int targetY = inst.Y.Value;

            var path = FindPath(curX, curY, targetX, targetY, loc, monitor);
            if (path == null || path.Count == 0)
            {
                LogWalkFailureDebug(curX, curY, targetX, targetY, loc, monitor);
                return new InstructionResult { Success = false, Action = "walkTo", Error = "无可达路径" };
            }

            player.controller = new PathFindController(path, player, loc)
            {
                nonDestructivePathing = true
            };

            monitor.Log($"walkTo ({targetX},{targetY})：路径 {path.Count} 步", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "walkTo" };
        }

        // =====================================================================
        // A* 寻路
        // =====================================================================

        /// <summary>
        /// 修复：原版用 SortedSet&lt;(f,g,x,y)&gt; 作 open set，
        /// 当两个节点 f、g 相同但坐标不同时 tuple 比较相等，
        /// 导致后一个节点被丢弃（SortedSet 不允许重复键）。
        ///
        /// 修复方案：改用优先队列（.NET 6+ PriorityQueue），
        /// 按 f 值排序，坐标单独存，彻底解决碰撞问题。
        /// </summary>
        private static Stack<Point> FindPath(
            int startX, int startY,
            int endX,   int endY,
            GameLocation loc,
            IMonitor monitor)
        {
            int mapW = loc.Map?.Layers?[0]?.LayerWidth  ?? 50;
            int mapH = loc.Map?.Layers?[0]?.LayerHeight ?? 50;

            if (endX < 0 || endY < 0 || endX >= mapW || endY >= mapH)
                return null;

            if (!IsWalkable(endX, endY, loc, mapW, mapH))
                return null;

            // (x, y) → 该节点当前最优 g 值
            var gScore   = new Dictionary<(int, int), int>();
            // (x, y) → 父节点
            var cameFrom = new Dictionary<(int, int), (int, int)>();
            // 已扩展节点
            var closed   = new HashSet<(int, int)>();

            // PriorityQueue<元素, 优先级>；优先级越小越先出队
            var openQueue = new PriorityQueue<(int x, int y), int>();

            var start = (x: startX, y: startY);
            var end   = (x: endX,   y: endY);

            gScore[start] = 0;
            openQueue.Enqueue(start, Heuristic(startX, startY, endX, endY));

            monitor.Log(
                $"A* 开始: ({startX},{startY})→({endX},{endY})  " +
                $"起点可走={IsWalkable(startX, startY, loc, mapW, mapH)}  " +
                $"终点可走={IsWalkable(endX,   endY,   loc, mapW, mapH)}",
                LogLevel.Info);

            int maxIter = 2000; // 适当放大，大地图够用
            int iter    = 0;

            while (openQueue.Count > 0 && iter++ < maxIter)
            {
                var cur = openQueue.Dequeue();

                if (closed.Contains(cur)) continue; // 已扩展过（优先队列里可能有重复入队）
                closed.Add(cur);

                if (cur == end)
                {
                    // 回溯路径
                    var path = new Stack<Point>();
                    var node = cur;
                    while (cameFrom.ContainsKey(node))
                    {
                        path.Push(new Point(node.x, node.y));
                        node = cameFrom[node];
                    }
                    monitor.Log($"A* 找到路径，{path.Count} 步，展开 {iter} 节点", LogLevel.Info);
                    return path;
                }

                int curG = gScore[cur];

                // 四方向
                Span<(int dx, int dy)> dirs = stackalloc (int, int)[]
                {
                    (0, -1), (0, 1), (-1, 0), (1, 0)
                };

                foreach (var (dx, dy) in dirs)
                {
                    var nb = (x: cur.x + dx, y: cur.y + dy);

                    if (closed.Contains(nb))                     continue;
                    if (!IsWalkable(nb.x, nb.y, loc, mapW, mapH)) continue;

                    int tentG = curG + 1;
                    if (gScore.TryGetValue(nb, out int existing) && tentG >= existing)
                        continue; // 不是更优路径

                    gScore[nb]   = tentG;
                    cameFrom[nb] = cur;
                    int f = tentG + Heuristic(nb.x, nb.y, endX, endY);
                    openQueue.Enqueue(nb, f); // 允许重复入队，出队时靠 closed 去重
                }
            }

            monitor.Log($"A* 探索 {iter} 个节点后无路径", LogLevel.Warn);
            return null;
        }

        private static int Heuristic(int x1, int y1, int x2, int y2)
            => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

        private static bool IsWalkable(int x, int y, GameLocation loc, int mapW, int mapH)
        {
            if (x < 0 || y < 0 || x >= mapW || y >= mapH)
                return false;

            // 玩家当前所在 tile 始终可通行（允许从障碍物上出发）
            int playerX = (int)(Game1.player.Position.X / 64f);
            int playerY = (int)(Game1.player.Position.Y / 64f);
            if (x == playerX && y == playerY)
                return true;

            var rect = new Rectangle(x * 64, y * 64, 64, 64);
            return !loc.isCollidingPosition(rect, Game1.viewport, true, 0, false, Game1.player);
        }

        private static void LogWalkFailureDebug(
            int curX, int curY, int targetX, int targetY,
            GameLocation loc, IMonitor monitor)
        {
            int mapW = loc.Map?.Layers?[0]?.LayerWidth  ?? 50;
            int mapH = loc.Map?.Layers?[0]?.LayerHeight ?? 50;

            monitor.Log($"walkTo ({targetX},{targetY}) 寻路失败，调试信息：", LogLevel.Warn);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int tx = targetX + dx, ty = targetY + dy;
                monitor.Log(
                    $"  目标周围 tile ({tx},{ty}): " +
                    $"{(IsWalkable(tx, ty, loc, mapW, mapH) ? "可走" : "不可走")}",
                    LogLevel.Warn);
            }
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int tx = curX + dx, ty = curY + dy;
                monitor.Log(
                    $"  起点周围 tile ({tx},{ty}): " +
                    $"{(IsWalkable(tx, ty, loc, mapW, mapH) ? "可走" : "不可走")}",
                    LogLevel.Warn);
            }
        }

        // =====================================================================
        // interact
        // =====================================================================

        private static InstructionResult ExecuteInteract(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "interact", Error = "缺少 X 或 Y" };

            var loc = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "interact", Error = "当前无位置" };

            bool didAction = loc.checkAction(
                new xTile.Dimensions.Location(inst.X.Value, inst.Y.Value),
                Game1.viewport,
                Game1.player);

            monitor.Log($"interact tile ({inst.X},{inst.Y}): {(didAction ? "成功" : "无响应")}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "interact" };
        }

        // =====================================================================
        // useItem
        // =====================================================================

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
            player.CurrentTool?.beginUsing(
                Game1.currentLocation,
                (int)player.Position.X,
                (int)player.Position.Y,
                player);

            monitor.Log($"useItem 槽位 {inst.Slot}: {item.Name}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "useItem" };
        }

        // =====================================================================
        // changeItem
        // =====================================================================

        private static InstructionResult ExecuteChangeItem(Instruction inst, IMonitor monitor)
        {
            if (inst.Slot == null)
                return new InstructionResult { Success = false, Action = "changeItem", Error = "缺少 Slot" };

            var player = Game1.player;
            if (inst.Slot.Value < 0 || inst.Slot.Value >= player.Items.Count)
                return new InstructionResult { Success = false, Action = "changeItem", Error = $"槽位 {inst.Slot} 超出范围" };

            player.CurrentToolIndex = inst.Slot.Value;
            var item = player.Items[inst.Slot.Value];

            monitor.Log($"changeItem 槽位 {inst.Slot}: {item?.Name ?? "空"}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "changeItem" };
        }

        // =====================================================================
        // talkTo
        // =====================================================================

        private static InstructionResult ExecuteTalkTo(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Npc))
                return new InstructionResult { Success = false, Action = "talkTo", Error = "缺少 NPC 名字" };

            var loc = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "talkTo", Error = "当前无位置" };

            NPC target = null;
            foreach (var character in loc.characters)
            {
                if (character is NPC npc &&
                    npc.Name.Equals(inst.Npc, StringComparison.OrdinalIgnoreCase))
                {
                    target = npc;
                    break;
                }
            }

            if (target == null)
                return new InstructionResult
                {
                    Success = false,
                    Action  = "talkTo",
                    Error   = $"NPC '{inst.Npc}' 不在当前地图"
                };

            target.checkAction(Game1.player, Game1.currentLocation);
            monitor.Log($"talkTo {inst.Npc}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "talkTo" };
        }

        // =====================================================================
        // emote — 修复映射错误
        // =====================================================================

        /// <summary>
        /// 星露谷表情 ID（来自游戏源码 Character.doEmote）：
        ///  4 = 心形（喜欢）   8 = 省略号（...）  12 = 音符（♪）
        /// 16 = 晕             20 = 叹号（！）      28 = 红X
        /// 32 = 怒火           36 = 问号（?）       40 = 惊喜（！+）
        /// 52 = 睡觉 Zzz       56 = 泪眼            60 = 怒火强
        ///
        /// 原版 bug：angry→12(音符)、sad→24(不存在) 已修正。
        /// </summary>
        private static readonly Dictionary<string, int> EmoteMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["happy"]    = 20,  // 叹号（开心感叹）
                ["sad"]      = 28,  // 红X（伤心/不满）
                ["angry"]    = 60,  // 怒火（愤怒）
                ["love"]     = 4,   // 心形（喜欢）
                ["surprise"] = 40,  // 惊喜
                ["sleep"]    = 52,  // Zzz
                ["note"]     = 12,  // 音符
                ["question"] = 36,  // 问号
                ["sweat"]    = 16,  // 晕/汗
                ["dots"]     = 8,   // 省略号
                ["cry"]      = 56,  // 泪眼
            };

        private static InstructionResult ExecuteEmote(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Text))
                return new InstructionResult { Success = false, Action = "emote", Error = "缺少表情" };

            if (int.TryParse(inst.Text, out int emoteId))
            {
                Game1.player.doEmote(emoteId);
                monitor.Log($"emote ID: {emoteId}", LogLevel.Info);
                return new InstructionResult { Success = true, Action = "emote" };
            }

            if (EmoteMap.TryGetValue(inst.Text, out int id))
            {
                Game1.player.doEmote(id);
                monitor.Log($"emote: {inst.Text} → ID {id}", LogLevel.Info);
                return new InstructionResult { Success = true, Action = "emote" };
            }

            return new InstructionResult
            {
                Success = false,
                Action  = "emote",
                Error   = $"未知表情: {inst.Text}（可用: {string.Join(", ", EmoteMap.Keys)}）"
            };
        }

        // =====================================================================
        // say
        // =====================================================================

        private static InstructionResult ExecuteSay(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Text))
                return new InstructionResult { Success = false, Action = "say", Error = "缺少文本" };

            Game1.chatBox.addMessage(inst.Text, Microsoft.Xna.Framework.Color.White);
            monitor.Log($"say: {inst.Text}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "say" };
        }

        // =====================================================================
        // wait
        // =====================================================================

        private static InstructionResult ExecuteWait(Instruction inst, IMonitor monitor)
        {
            int ms = inst.DurationMs ?? 1000;
            monitor.Log($"wait {ms}ms", LogLevel.Info);
            // 实际等待由 ModEntry.waitTicksRemaining 控制
            return new InstructionResult { Success = true, Action = "wait" };
        }
    }
}
