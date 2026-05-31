# AI Companion — 星露谷物语 AI 玩伴 Mod

> 最后更新：2026-05-31 14:28 GMT+8
> 游戏路径：`C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley`

---

## 一、项目目标

**做一个 AI 伙伴，陪主人一起玩星露谷。**

通过局域网联机，AI 控制第二个角色，能和主人对话、一起行动。

---

## 二、已完成 ✅

| 功能 | 状态 | 说明 |
|------|------|------|
| 双实例局域网联机 | ✅ | 主人主持农场，AI 加入局域网 |
| 进程数自动检测 | ✅ | 1进程=主机模式，2进程=AI模式 |
| AI 模式激活 | ✅ | 加入者自动进入 AI 模式 |
| 游戏状态读取 | ✅ | 位置、时间、体力、背包等 |
| 指令执行 | ✅ | 移动、传送、说话、等待等 |
| 聊天检测 | ✅ | Mod 能检测玩家在游戏里发的消息 |
| Python 监听器 | ✅ | Python 读取玩家消息并打印 |
| AI 回话（本地） | ✅ | AI 能在自己的游戏实例里说话 |

---

## 三、核心问题 ❌

### 跨实例聊天（未解决）

**目标**：AI 回复的消息在主机聊天框显示。

**已尝试的方案**：

| 方案 | 结果 | 原因 |
|------|------|------|
| `Game1.chatBox.addMessage()` | ❌ 只在本地显示 | 不走多人网络 |
| `helper.Multiplayer.SendMessage()` | ❌ 无效果 | SMAPI Mod 间通信，不触发聊天框 |
| `sendChatMessage(LanguageCode, String, Int64)` | ❌ 广播成功但不显示 | 可能只在主机端有效，客户端无效 |
| 写 reply.json + 主机读取 | ❌ 待验证 | 主机 Mod 是否在运行？ |

**当前方案**：
```
AI 实例 → 写 reply.json → 主机实例读取 → addMessage 显示
```

**需要调试**：
1. 主机实例的 Mod 是否在运行？
2. reply.json 是否被正确读取？
3. addMessage 是否在主机端生效？

---

## 四、架构

```
┌──────────────┐                    ┌──────────────┐
│   游戏实例1   │    局域网联机      │   游戏实例2   │
│  (主人控制)   │◄──────────────────►│  (AI 控制)    │
│  主机模式     │    游戏内同步       │  AI 模式     │
└──────────────┘                    └──────┬───────┘
                                          │
                                    state.json │ instruction.json
                                          │
                                    ┌─────▼─────┐
                                    │   Python   │
                                    │  监听器    │
                                    └─────┬─────┘
                                          │
                                    ┌─────▼─────┐
                                    │  OpenClaw  │
                                    │  (AI 决策) │
                                    └───────────┘
```

### 通信流程

```
玩家打字 → Mod 检测 → chat.json → Python 读取 → 打印到控制台
                                                      ↓
                                              OpenClaw 看到消息
                                                      ↓
                                              OpenClaw 写指令
                                                      ↓
Mod 执行 ← instruction.json ← Python ← OpenClaw
```

---

## 五、文件结构

```
AICompanion/                    # 源码目录
├── AICompanion.csproj
├── manifest.json
├── ModEntry.cs                 # 主入口，自动检测身份
├── GameConfig.cs               # 通信路径配置
├── GameStateReader.cs          # 读取游戏状态
├── InstructionExecutor.cs      # 执行指令
├── ChatBroadcaster.cs          # 聊天广播器（核心问题）
├── chat_listener.py            # Python 监听器
└── ai_bridge.py                # Python 控制脚本（待完善）

游戏目录/Mods/AICompanion/       # Mod 部署目录
├── AICompanion.dll
└── manifest.json

游戏目录/ai/                     # 通信目录
├── state.json                  # 游戏状态
├── instruction.json            # 指令文件
├── chat.json                   # 聊天消息
├── reply.json                  # AI 回复（主机读取）
└── persona.json                # AI 人设配置
```

---

## 六、下一步

### 优先级 1：跨实例聊天（核心问题）

**需要调试**：
1. 在主机 Mod 的 OnHostTick 加日志，确认是否在运行
2. 检查 reply.json 是否被读取
3. 检查 addMessage 是否生效

### 优先级 2：LLM 集成

**目标**：玩家说话 → AI 理解 → 自主决策。

### 优先级 3：动作执行

**目标**：AI 能执行浇水、挖矿、砍树、钓鱼等动作。

---

## 七、GitHub

https://github.com/LeoDS95/AICompanion

---

## 八、里程碑

| 日期 | 里程碑 | 状态 |
|------|--------|------|
| 2026-05-31 | 双实例局域网联机 | ✅ |
| 2026-05-31 | 进程数自动检测 | ✅ |
| 2026-05-31 | AI 模式激活 | ✅ |
| 2026-05-31 | 游戏状态读取 | ✅ |
| 2026-05-31 | 指令执行 | ✅ |
| 2026-05-31 | 聊天检测 | ✅ |
| 2026-05-31 | Python 监听器 | ✅ |
| 2026-05-31 | 跨实例聊天 | ❌ 待解决 |
| TBD | LLM 集成 | ❌ 待开发 |
| TBD | 动作执行 | ❌ 待开发 |
