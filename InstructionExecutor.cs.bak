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
    /// <summary>
    /// 指令结构
    /// </summary>
    public class Instruction
    {
        public string Action { get; set; }       // moveTo, interact, useItem, changeItem, talkTo, emote, say, wait
        public int? X { get; set; }              // moveTo/interact 的目标 X
        public int? Y { get; set; }              // moveTo/interact 的目标 Y
        public int? Slot { get; set; }           // useItem/changeItem 的背包槽位
        public string Npc { get; set; }          // talkTo 的 NPC 名字
        public string Text { get; set; }         // say 的文本 / emote 的表情名
        public int? DurationMs { get; set; }     // wait 的等待时长
    }

    /// <summary>
    /// 指令执行结果
    /// </summary>
    public class InstructionResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Action { get; set; }
    }

    public static class InstructionExecutor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// 读取 instruction.json，返回指令（没有文件或解析失败返回 null）
        /// 注意：读取后不删除，由调用方在执行成功后调用 ConfirmConsumed 删除
        /// </summary>
        public static Instruction ReadInstruction(IMonitor monitor)
        {
            try
            {
                if (!File.Exists(GameConfig.InstructionFile))
                    return null;

                var json = File.ReadAllText(GameConfig.InstructionFile);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return JsonSerializer.Deserialize<Instruction>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                monitor.Log($"读取指令出错: {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        /// <summary>
        /// 确认指令已消费，删除指令文件
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
                monitor.Log($"删除指令文件出错: {ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// 执行指令
        /// </summary>
        public static InstructionResult Execute(Instruction instruction, IMonitor monitor, IModHelper helper = null)
        {
            if (instruction == null || string.IsNullOrEmpty(instruction.Action))
                return new InstructionResult { Success = false, Error = "空指令" };

            try
            {
                switch (instruction.Action.ToLower())
                {
                    case "moveto":
                        return ExecuteMoveTo(instruction, monitor);
                    case "interact":
                        return ExecuteInteract(instruction, monitor);
                    case "useitem":
                        return ExecuteUseItem(instruction, monitor);
                    case "changeitem":
                        return ExecuteChangeItem(instruction, monitor);
                    case "talkto":
                        return ExecuteTalkTo(instruction, monitor);
                    case "emote":
                        return ExecuteEmote(instruction, monitor);
                    case "say":
                        return ExecuteSay(instruction, monitor, helper);
                    case "locate":
                        return ExecuteLocate(monitor);
                    case "warpto":
                        return ExecuteWarpTo(instruction, monitor);
                    case "walkto":
                        return ExecuteWalkTo(instruction, monitor);
                    case "wait":
                        return ExecuteWait(instruction, monitor);
                    default:
                        return new InstructionResult
                        {
                            Success = false,
                            Action = instruction.Action,
                            Error = $"未知指令: {instruction.Action}"
                        };
                }
            }
            catch (Exception ex)
            {
                monitor.Log($"执行指令 [{instruction.Action}] 出错: {ex.Message}", LogLevel.Warn);
                return new InstructionResult
                {
                    Success = false,
                    Action = instruction.Action,
                    Error = ex.Message
                };
            }
        }

        // ===== 指令实现 =====

        /// <summary>
        /// 移动到指定坐标（简单实现：直接设置位置）
        /// TODO: 后续改为路径行走
        /// </summary>
        private static InstructionResult ExecuteMoveTo(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "moveTo", Error = "缺少 X 或 Y" };

            var player = Game1.player;
            float targetX = inst.X.Value * 64f;
            float targetY = inst.Y.Value * 64f;

            player.Position = new Microsoft.Xna.Framework.Vector2(targetX, targetY);
            monitor.Log($"移动到 tile ({inst.X}, {inst.Y})，像素 ({targetX}, {targetY})", LogLevel.Info);

            return new InstructionResult { Success = true, Action = "moveTo" };
        }

        /// <summary>
        /// 走到指定坐标（使用游戏内建寻路，角色自己走，有动画）
        /// </summary>
        /// <summary>
        /// 镜头定位到角色当前位置
        /// </summary>
        private static InstructionResult ExecuteLocate(IMonitor monitor)
        {
            var player = Game1.player;
            Game1.viewport.X = (int)player.Position.X - Game1.viewport.Width / 2;
            Game1.viewport.Y = (int)player.Position.Y - Game1.viewport.Height / 2;
            monitor.Log($"镜头定位到 ({player.Position.X / 64:F0},{player.Position.Y / 64:F0})", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "locate" };
        }

        /// <summary>
        /// 传送到指定地图的指定坐标（用于出门/换地图）
        /// </summary>
        private static InstructionResult ExecuteWarpTo(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Npc))  // 复用 Npc 字段传目标地图名
                return new InstructionResult { Success = false, Action = "warpTo", Error = "缺少目标地图" };
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "warpTo", Error = "缺少 X 或 Y" };

            string targetMap = inst.Npc;
            int tileX = inst.X.Value;
            int tileY = inst.Y.Value;

            Game1.warpFarmer(targetMap, tileX, tileY, false);
            monitor.Log($"warpTo {targetMap} ({tileX},{tileY})", LogLevel.Info);

            return new InstructionResult { Success = true, Action = "warpTo" };
        }

        private static InstructionResult ExecuteWalkTo(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "walkTo", Error = "缺少 X 或 Y" };

            var player = Game1.player;
            var loc = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "walkTo", Error = "当前无位置" };

            int curX = (int)(player.Position.X / 64f);
            int curY = (int)(player.Position.Y / 64f);
            int targetX = inst.X.Value;
            int targetY = inst.Y.Value;

            // 使用游戏内建寻路（不传自定义 A* 路径）
            player.controller = new PathFindController(player, loc, new Point(targetX, targetY), -1);
            player.controller.nonDestructivePathing = false;

            if (player.controller.pathToEndPoint == null || player.controller.pathToEndPoint.Count == 0)
            {
                player.controller = null;
                monitor.Log($"walkTo ({targetX},{targetY}) 内建寻路失败", LogLevel.Warn);
                return new InstructionResult { Success = false, Action = "walkTo", Error = "内建寻路失败" };
            }

            monitor.Log($"walkTo ({targetX},{targetY})：内建寻路，路径 {player.controller.pathToEndPoint.Count} 步", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "walkTo" };
        }

        /// <summary>
        /// A* 寻路（带碰撞检测）
        /// </summary>
        private static Stack<Point> FindPath(int startX, int startY, int endX, int endY, GameLocation loc, IMonitor monitor)
        {
            int mapW = loc.Map?.Layers?[0]?.LayerWidth ?? 50;
            int mapH = loc.Map?.Layers?[0]?.LayerHeight ?? 50;

            if (endX < 0 || endY < 0 || endX >= mapW || endY >= mapH)
                return null;

            if (!IsWalkable(endX, endY, loc, mapW, mapH))
                return null;

            var openSet = new SortedSet<(int f, int g, int x, int y)>();
            var cameFrom = new Dictionary<(int x, int y), (int x, int y)>();
            var gScore = new Dictionary<(int x, int y), int>();
            var closed = new HashSet<(int x, int y)>();

            var start = (x: startX, y: startY);
            var end = (x: endX, y: endY);

            gScore[start] = 0;
            openSet.Add((Heuristic(startX, startY, endX, endY), 0, startX, startY));

            int maxIter = 1000;
            int iter = 0;

            monitor.Log($"A* 开始: ({startX},{startY})->({endX},{endY})，起点可走={IsWalkable(startX, startY, loc, mapW, mapH)}，终点可走={IsWalkable(endX, endY, loc, mapW, mapH)}", LogLevel.Info);

            while (openSet.Count > 0 && iter++ < maxIter)
            {
                var current = openSet.Min;
                openSet.Remove(current);
                var cur = (x: current.x, y: current.y);

                if (iter <= 5)
                    monitor.Log($"A* 展开 #{iter}: ({cur.x},{cur.y}) g={current.g} f={current.f}", LogLevel.Info);

                if (cur == end)
                {
                    var path = new Stack<Point>();
                    var node = cur;
                    while (cameFrom.ContainsKey(node))
                    {
                        path.Push(new Point(node.x, node.y));
                        node = cameFrom[node];
                    }
                    monitor.Log($"A* 找到路径，{path.Count} 步", LogLevel.Info);
                    return path;
                }

                closed.Add(cur);

                int[][] dirs = { new[] { 0, -1 }, new[] { 0, 1 }, new[] { -1, 0 }, new[] { 1, 0 } };
                foreach (var d in dirs)
                {
                    int nx = cur.x + d[0];
                    int ny = cur.y + d[1];
                    var neighbor = (x: nx, y: ny);

                    if (closed.Contains(neighbor)) continue;
                    bool walkable = IsWalkable(nx, ny, loc, mapW, mapH);
                    if (iter <= 5)
                        monitor.Log($"  邻居 ({nx},{ny}): {(walkable ? "可走" : "不可走")}", LogLevel.Info);
                    if (!walkable) continue;

                    int tentG = gScore[cur] + 1;
                    if (!gScore.ContainsKey(neighbor) || tentG < gScore[neighbor])
                    {
                        cameFrom[neighbor] = cur;
                        gScore[neighbor] = tentG;
                        int f = tentG + Heuristic(nx, ny, endX, endY);
                        openSet.Add((f, tentG, nx, ny));
                    }
                }
            }

            monitor.Log($"A* 探索 {iter} 个 tile，无路径", LogLevel.Warn);
            return null;
        }

        private static int Heuristic(int x1, int y1, int x2, int y2)
        {
            return Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
        }

        /// <summary>
        /// 检查 tile 是否可行走
        /// </summary>
        private static bool IsWalkable(int x, int y, GameLocation loc, int mapW, int mapH)
        {
            if (x < 0 || y < 0 || x >= mapW || y >= mapH)
                return false;

            // 玩家当前位置始终可通行（允许从障碍物上离开）
            int playerX = (int)(Game1.player.Position.X / 64f);
            int playerY = (int)(Game1.player.Position.Y / 64f);
            if (x == playerX && y == playerY)
                return true;

            // isTilePassable 只检查 Back/Buildings/Front 层 tile 属性，不管家具碰撞体
            return loc.isTilePassable(new xTile.Dimensions.Location(x, y), Game1.viewport);
        }

        /// <summary>
        /// 与指定坐标的对象交互
        /// </summary>
        private static InstructionResult ExecuteInteract(Instruction inst, IMonitor monitor)
        {
            if (inst.X == null || inst.Y == null)
                return new InstructionResult { Success = false, Action = "interact", Error = "缺少 X 或 Y" };

            var loc = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "interact", Error = "当前无位置" };

            // 尝试与该位置的物体交互
            var tile = new Microsoft.Xna.Framework.Vector2(inst.X.Value, inst.Y.Value);
            bool didAction = loc.checkAction(
                new xTile.Dimensions.Location(inst.X.Value, inst.Y.Value),
                Game1.viewport,
                Game1.player
            );

            monitor.Log($"交互 tile ({inst.X}, {inst.Y}): {(didAction ? "成功" : "无响应")}", LogLevel.Info);
            return new InstructionResult { Success = true, Action = "interact" };
        }

        /// <summary>
        /// 使用背包中指定槽位的物品
        /// </summary>
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
            player.CurrentTool?.beginUsing(Game1.currentLocation, (int)player.Position.X, (int)player.Position.Y, player);
            monitor.Log($"使用槽位 {inst.Slot}: {item.Name}", LogLevel.Info);

            return new InstructionResult { Success = true, Action = "useItem" };
        }

        /// <summary>
        /// 切换当前选中的物品槽位
        /// </summary>
        private static InstructionResult ExecuteChangeItem(Instruction inst, IMonitor monitor)
        {
            if (inst.Slot == null)
                return new InstructionResult { Success = false, Action = "changeItem", Error = "缺少 Slot" };

            var player = Game1.player;
            if (inst.Slot.Value < 0 || inst.Slot.Value >= player.Items.Count)
                return new InstructionResult { Success = false, Action = "changeItem", Error = $"槽位 {inst.Slot} 超出范围" };

            player.CurrentToolIndex = inst.Slot.Value;
            var item = player.Items[inst.Slot.Value];
            monitor.Log($"切换到槽位 {inst.Slot}: {item?.Name ?? "空"}", LogLevel.Info);

            return new InstructionResult { Success = true, Action = "changeItem" };
        }

        /// <summary>
        /// 与 NPC 对话
        /// </summary>
        private static InstructionResult ExecuteTalkTo(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Npc))
                return new InstructionResult { Success = false, Action = "talkTo", Error = "缺少 NPC 名字" };

            var loc = Game1.currentLocation;
            if (loc == null)
                return new InstructionResult { Success = false, Action = "talkTo", Error = "当前无位置" };

            // 查找 NPC
            NPC target = null;
            foreach (var character in loc.characters)
            {
                if (character is NPC npc && npc.Name.Equals(inst.Npc, StringComparison.OrdinalIgnoreCase))
                {
                    target = npc;
                    break;
                }
            }

            if (target == null)
                return new InstructionResult { Success = false, Action = "talkTo", Error = $"NPC '{inst.Npc}' 不在当前地图" };

            // 触发对话
            target.checkAction(Game1.player, Game1.currentLocation);
            monitor.Log($"与 {inst.Npc} 对话", LogLevel.Info);

            return new InstructionResult { Success = true, Action = "talkTo" };
        }

        /// <summary>
        /// 显示表情
        /// </summary>
        private static InstructionResult ExecuteEmote(Instruction inst, IMonitor monitor)
        {
            if (string.IsNullOrEmpty(inst.Text))
                return new InstructionResult { Success = false, Action = "emote", Error = "缺少表情" };

            // 星露谷的表情 ID: 0=空心心, 1=实心心, 2=怒, 3=叹号, 4=问号, 5=睡觉, 8=省略号, 12=音符, 16=晕, 20=红叉, 24=红心, 28=红X, 32=红怒, 36=骷髅, 40=惊喜, etc.
            if (int.TryParse(inst.Text, out int emoteId))
            {
                Game1.player.doEmote(emoteId);
                monitor.Log($"表情: {emoteId}", LogLevel.Info);
            }
            else
            {
                // 文本表情映射
                var emoteMap = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["happy"] = 20, ["sad"] = 24, ["angry"] = 12,
                    ["love"] = 0, ["surprise"] = 40, ["sleep"] = 5,
                    ["note"] = 12, ["question"] = 40, ["shock"] = 16
                };
                if (emoteMap.TryGetValue(inst.Text, out int id))
                {
                    Game1.player.doEmote(id);
                    monitor.Log($"表情: {inst.Text} ({id})", LogLevel.Info);
                }
                else
                {
                    return new InstructionResult { Success = false, Action = "emote", Error = $"未知表情: {inst.Text}" };
                }
            }

            return new InstructionResult { Success = true, Action = "emote" };
        }

        /// <summary>
        /// 在聊天框说话
        /// </summary>
        private static InstructionResult ExecuteSay(Instruction inst, IMonitor monitor, IModHelper helper = null)
        {
            if (string.IsNullOrEmpty(inst.Text))
                return new InstructionResult { Success = false, Action = "say", Error = "缺少文本" };

            // 通过游戏多人协议广播消息
            bool ok = ChatBroadcaster.SendMessage(inst.Text);

            monitor.Log($"说话({(ok ? "广播" : "失败")}): {inst.Text}", LogLevel.Info);
            return new InstructionResult { Success = ok, Action = "say", Error = ok ? null : "广播失败" };
        }

        /// <summary>
        /// 等待指定毫秒（标记等待状态，不阻塞游戏线程）
        /// </summary>
        private static InstructionResult ExecuteWait(Instruction inst, IMonitor monitor)
        {
            int ms = inst.DurationMs ?? 1000;
            monitor.Log($"等待 {ms}ms", LogLevel.Info);
            // 实际等待通过 ModEntry 的 tick 计数器实现，这里只标记成功
            return new InstructionResult { Success = true, Action = "wait" };
        }
    }
}
