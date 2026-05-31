# AI Companion — 星露谷物语 AI 玩伴 Mod

> 最后更新：2026-05-31 11:30 GMT+8
> 游戏路径：`C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley`

---

## 一、项目概述

**目标**：SMAPI Mod 让 AI 控制游戏角色，通过局域网联机陪主人玩星露谷。

**核心原则**：
- Mod 越薄越好（纯执行器，不含决策逻辑）
- 决策全在 Python 侧（天然支持大模型驱动）
- 通过 JSON 文件通信，无网络依赖

---

## 二、联机方案

### 双实例局域网联机

从文件夹启动第二个游戏实例，一个选「合作→主持农场」，另一个选「加入局域网→不输入IP」，直接以第二个角色加入。

```
┌──────────────┐         ┌──────────────┐
│   游戏实例1   │  局域网  │   游戏实例2   │
│  (主人手动)   │◄───────►│  (AI 控制)    │
│  主持农场     │  自动    │  加入局域网   │
└──────────────┘  连接    └──────┬───────┘
                                │
                          SMAPI + AICompanion
                                │
                          state.json ──► Python ──► LLM
```

### 优势

- ✅ 零网络代码，游戏自带联机
- ✅ 完整游戏同步（位置、动作、聊天）
- ✅ 主人存档不变，游戏2是新角色
- ✅ 游戏内聊天，AI 可通过 say 指令说话

### Mod 自动检测身份

Mod 启动时自动判断：
- **主机（`Context.IsMainPlayer`）**→ 主人模式，不干预，只写状态供监控
- **加入者（`!Context.IsMainPlayer`）**→ AI 模式，读状态 + 执行指令

主人只需手动启动两个游戏实例，Mod 自动切换模式。

---

## 三、架构

```
┌──────────────┐                    ┌──────────────┐
│   游戏实例1   │    局域网联机      │   游戏实例2   │
│  (主人控制)   │◄──────────────────►│  (AI 控制)    │
│  无Mod       │    游戏内同步       │  AICompanion │
└──────────────┘                    └──────┬───────┘
                                          │
                                    state.json │ instruction.json
                                          │
                                    ┌─────▼─────┐
                                    │   Python   │
                                    │  (AI 决策) │
                                    └─────┬─────┘
                                          │
                                    ┌─────▼─────┐
                                    │  大模型 API │
                                    └───────────┘
```

### 通信流程

```
每0.5秒: AI Mod → 读游戏状态 → 写 state.json
每0.5秒: AI Mod → 读 instruction.json → 执行成功则删除，失败保留重试
Python: 读 state.json → 规划 → 写 instruction.json
```

### 通信目录

`C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\ai\`

| 文件 | 方向 | 说明 |
|------|------|------|
| `state.json` | Mod → Python | 游戏状态快照 |
| `instruction.json` | Python → Mod | 单条指令，读后删除 |
| `persona.json` | 玩家编辑 → Python | AI 人设配置 |

---

## 四、当前已实现

### 指令集

| 指令 | 参数 | 说明 |
|------|------|------|
| `moveTo` | X, Y | 瞬移（调试用） |
| `walkTo` | X, Y | 走路（游戏内建寻路） |
| `interact` | X, Y | 与物体交互 |
| `useItem` | Slot | 使用背包物品 |
| `changeItem` | Slot | 切换物品槽位 |
| `talkTo` | Npc | 与 NPC 对话 |
| `emote` | Text | 显示表情 |
| `say` | Text | 聊天框说话 |
| `wait` | DurationMs | 等待 |
| `locate` | — | 镜头定位到角色 |
| `warpTo` | 地图名, X, Y | 地图传送 |

### state.json 字段

```json
{
  "PlayerName": "AI角色名",
  "PlayerX": 576, "PlayerY": 608,
  "Health": 100, "MaxHealth": 100,
  "Energy": 270, "MaxEnergy": 270,
  "Gold": 500,
  "FarmingLevel": 0, "MiningLevel": 0, "FishingLevel": 0, "CombatLevel": 0,
  "LocationName": "FarmHouse",
  "Season": "spring", "Day": 1, "Year": 1,
  "TimeOfDay": 600, "TimeString": "06:00",
  "Weather": "晴天",
  "NpcCount": 0, "MonsterCount": 0,
  "ItemCount": 5,
  "InventorySummary": "Axex1, Hoex1, Watering Canx1, Pickaxex1, Scythex1",
  "WaitingForInstruction": true,
  "IsWalking": false,
  "WalkStepsRemaining": 0,
  "IsMultiplayer": true,
  "IsMainPlayer": false,
  "PlayerCount": 2,
  "LastError": null
}
```

### 修复记录

| 问题 | 原因 | 修复 |
|------|------|------|
| 室内寻路走不动 | `isCollidingPosition` 触发家具碰撞 | 改用 `isTilePassable` |
| 寻路太慢 | 自定义 A* 慢 | 改用游戏内建 `PathFindController` |
| 指令重复执行 | 文件删除有延迟 | 执行成功才删，失败保留重试 |
| emote 鬼畜 | 同一指令重复读取 | 加去重 hash |

---

## 五、开发计划

### P0 — 核心功能

| 序号 | 任务 | 说明 |
|------|------|------|
| 1 | Action Queue | Python 侧维护队列，一次规划多步 |
| 2 | IsBusy | 动画完成态，防止指令堆积 |
| 3 | 暂停游戏时钟 | 等待指令时时钟暂停 |
| 4 | PerceivedObjects | 传送点 + 白名单 |
| 5 | 玩家行为感知 | AI 知道主人在做什么 |

### P1 — 智能层

| 序号 | 任务 | 说明 |
|------|------|------|
| 6 | 陪伴行为模式 | 根据主人行为切换 AI 模式 |
| 7 | Goal 系统 | 长期目标，减少决策空间 |
| 8 | Persona 配置 | persona.json 注入人设 |
| 9 | 联机状态感知 | AI 知道主人在哪、在做什么 |

---

## 六、文件结构

```
AICompanion/                    # 源码目录
├── AICompanion.csproj
├── manifest.json
├── ModEntry.cs                 # 主入口，自动检测身份
├── GameConfig.cs               # 通信路径配置
├── GameStateReader.cs          # 读取游戏状态
├── InstructionExecutor.cs      # 执行指令
└── ai_bridge.py                # Python 端（待完善）

游戏目录/Mods/AICompanion/       # Mod 部署目录
├── AICompanion.dll
└── manifest.json

游戏目录/ai/                     # 通信目录
├── state.json
├── instruction.json
└── persona.json
```

---

## 七、里程碑

| 日期 | 里程碑 | 状态 |
|------|--------|------|
| 2026-05-31 | 联机 + AI 控制全链路打通 | ✅ 完成 |
| 2026-05-31 | 进程数检测（1进程=主机，2进程=AI） | ✅ 完成 |
| 2026-05-31 | Python Bridge 基础通信 | ✅ 完成 |
| TBD | Action Queue（多步规划） | 待开发 |
| TBD | LLM 决策集成 | 待开发 |

https://github.com/LeoDS95/AICompanion
