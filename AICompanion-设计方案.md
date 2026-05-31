# AI Companion — 星露谷物语 AI 玩伴 Mod

> 最后更新：2026-05-31 17:21 GMT+8
> 游戏路径：`C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley`
> GitHub：https://github.com/LeoDS95/AICompanion

---

## 一、项目目标

**做一个 AI 伙伴，陪主人一起玩星露谷。**

通过局域网联机，AI 控制第二个角色，能和主人对话、一起行动。

### 核心架构

```
主机（主人手动控制）←→ 局域网联机 ←→ AI实例（Mod控制）
                                        ↓
                                   state.json → Python → OpenClaw
                                        ↑
                                   instruction.json ← Python ← OpenClaw
```

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
| AI 控制角色移动 | ✅ | AI 可控制第二角色移动、传送 |
| 跨实例通信（SendMessage） | ✅ | SMAPI ModMessage 通信链路打通 |

---

## 三、核心问题 ❌

### 跨实例聊天显示（未解决）

**目标**：AI 回复的消息显示在**主机**的游戏画面上。

**成功过的方案**：
```csharp
// AI 实例发送
Helper.Multiplayer.SendMessage(
    message: text,
    messageType: "AIChat",
    modIDs: new[] { ModManifest.UniqueID }
);

// 主机接收（ModMessageReceived 事件在主线程触发）
private void OnMessageReceived(object sender, ModMessageReceivedEventArgs e)
{
    _chatWindow?.ShowSubtitle($"AI: {msg}");
}
```

**成功时的现象**：
- 日志显示 `[AI聊天] 字幕显示: 2: 嘿~ 主人！我在呢！`
- 主人确认看到了字幕（但字太小）

**失败原因**：
- 改字幕位置/大小时，不小心把通信机制从 SendMessage 改成了文件读取
- 文件读取可能在后台线程，导致 UI 更新被游戏静默忽略

**目前状态**：
- SendMessage 通信链路已恢复
- 日志显示字幕在 AI 实例上显示，不是主机
- 主机日志显示 `[AI聊天] 广播` 但没有 `[AI聊天] 字幕显示`

---

## 四、已测试的方案

| 方案 | 结果 | 原因 |
|------|------|------|
| `Game1.chatBox.addMessage()` | ❌ 只在本地显示 | 不走多人网络 |
| `Helper.Multiplayer.SendMessage()` | ⚠️ 成功过一次 | 后来改代码时搞坏了 |
| `sendChatMessage(LanguageCode, String, Int64)` | ❌ 广播成功但不显示 | 可能只在主机端有效 |
| 写 reply.json + 主机读取 | ❌ 主机不显示 | 文件读取在后台线程，UI 更新被忽略 |
| 自建 UI 窗口（T 键） | ❌ 输入没反应 | 键盘输入处理有问题 |
| 字幕显示（工具栏上方） | ⚠️ 在 AI 实例显示 | 主机收不到消息 |

---

## 五、关键技术要点

### 1. UI 线程安全

**关键洞察**（来自 Gemini）：
- `ModMessageReceived` 事件在**主线程**触发 → UI 更新安全
- 文件读取（FileSystemWatcher、Task.Run）在**后台线程** → UI 更新被游戏静默忽略

### 2. 跨实例通信

**正确方案**：
```csharp
// 发送端（AI 实例）
Helper.Multiplayer.SendMessage(message, "AIChat", modIDs: new[] { ModManifest.UniqueID });

// 接收端（主机，主线程）
Helper.Events.Multiplayer.ModMessageReceived += OnMessageReceived;
```

**注意**：
- `ModMessageReceived` 在 SMAPI 4.x 中是正确的事件名
- 事件在 Entry() 中注册，两端都能收到

### 3. 自建 UI 键盘输入

**问题**：自建 UI 窗口收不到键盘输入

**解决方案**（来自 Gemini）：
```csharp
// 设置键盘焦点
Game1.keyboardDispatcher.Subscriber = this.chatBox;

// 关闭时释放
public override void cleanupBeforeExit()
{
    if (Game1.keyboardDispatcher.Subscriber == this.chatBox)
        Game1.keyboardDispatcher.Subscriber = null;
    base.cleanupBeforeExit();
}
```

---

## 六、文件结构

```
AICompanion/                    # 源码目录
├── AICompanion.csproj
├── manifest.json
├── ModEntry.cs                 # 主入口，事件注册
├── GameConfig.cs               # 通信路径配置
├── GameStateReader.cs          # 读取游戏状态
├── InstructionExecutor.cs      # 执行指令
├── ChatWindow.cs               # 字幕显示窗口
├── ChatBroadcaster.cs          # 聊天广播器（备用）
├── chat_listener.py            # Python 监听器
└── ai_bridge.py                # Python 控制脚本（待完善）

游戏目录/Mods/AICompanion/       # Mod 部署目录
├── AICompanion.dll
└── manifest.json

游戏目录/ai/                     # 通信目录
├── state.json                  # 游戏状态
├── instruction.json            # 指令文件
├── chat.json                   # 聊天消息
└── persona.json                # AI 人设配置
```

---

## 七、下一步

### 优先级 1：修复跨实例聊天显示

**需要解决**：
- 为什么 SendMessage 在 AI 实例触发了 ModMessageReceived，但主机没有？
- 可能原因：主机的 Mod 没有正确注册事件，或者消息没有广播到主机

**调试方向**：
- 在主机的 Entry() 中加日志，确认事件注册成功
- 在 OnMessageReceived 中加日志，确认主机是否收到消息
- 检查 ModManifest.UniqueID 是否一致

### 优先级 2：LLM 集成

**目标**：玩家说话 → AI 理解 → 自主决策

### 优先级 3：动作执行

**目标**：AI 能执行浇水、挖矿、砍树、钓鱼等动作

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
| 2026-05-31 | SendMessage 通信链路 | ✅ |
| 2026-05-31 | 跨实例聊天显示 | ❌ 待解决 |
| TBD | LLM 集成 | ❌ 待开发 |
| TBD | 动作执行 | ❌ 待开发 |
