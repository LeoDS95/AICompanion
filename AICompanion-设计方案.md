# AI Companion — 星露谷物语 AI 玩伴 Mod

> 最后更新：2026-05-31 17:43 GMT+8
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
| **跨实例聊天显示** | ✅ | **AI 回复显示在主机聊天框** |

---

## 三、跨实例聊天方案（成功！）

### 最终方案（Claude 方案 A）

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
    if (e.FromModID != ModManifest.UniqueID) return;
    if (e.Type != "AIChat") return;

    var msg = e.ReadAs<string>();

    // 调试日志
    Monitor.Log($"[AI聊天] 主机收到: {msg}，chatWindow={_chatWindow != null}", LogLevel.Info);

    // 直接写聊天框，不用自定义窗口
    Game1.chatBox?.addMessage($"[AI] {msg}", Color.Cyan);
}
```

### 关键修复

1. **只有 AI 实例处理指令**（主机不处理 instruction.json）
2. **用 `Game1.chatBox?.addMessage()`**（不用自定义 ChatWindow）
3. **SendMessage 通信链路**（ModMessageReceived 在主线程触发，UI 更新安全）

### 已测试的失败方案

| 方案 | 结果 | 原因 |
|------|------|------|
| `Game1.chatBox.addMessage()` | ❌ 只在本地显示 | 不走多人网络 |
| `sendChatMessage(LanguageCode, String, Int64)` | ❌ 广播成功但不显示 | 可能只在主机端有效 |
| 写 reply.json + 主机读取 | ❌ 主机不显示 | 文件读取在后台线程，UI 更新被忽略 |
| 自建 UI 窗口（T 键） | ❌ 输入没反应 | 键盘输入处理有问题 |
| `_chatWindow?.ShowSubtitle()` | ❌ 主机不显示 | `_chatWindow` 可能为 null |

### 技术要点

- **UI 线程安全**：`ModMessageReceived` 在主线程触发，文件读取可能在后台线程
- **SendMessage 只要 modID 和 messageType 匹配就行**，版本不影响
- **两端 Mod 版本不需要完全一致**

---

## 四、待完成

| 优先级 | 任务 | 说明 |
|--------|------|------|
| 1 | 修 warpTo bug | InstructionExecutor 里没实现 |
| 2 | 修表情 ID | happy=4, note=12, angry=28 |
| 3 | SimpleAI 基准测试 | 跑完整 Farm 日常循环 |
| 4 | 接 LLM | 用 OpenClaw 或其他大模型 |

---

## 五、文件结构

```
AICompanion/                    # 源码目录
├── AICompanion.csproj
├── manifest.json
├── ModEntry.cs                 # 主入口，事件注册
├── GameConfig.cs               # 通信路径配置
├── GameStateReader.cs          # 读取游戏状态
├── InstructionExecutor.cs      # 执行指令
├── ChatWindow.cs               # 字幕显示窗口（备用）
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

## 六、里程碑

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
| 2026-05-31 | **跨实例聊天显示** | ✅ |
| TBD | warpTo + 表情 ID 修复 | ❌ |
| TBD | SimpleAI 基准测试 | ❌ |
| TBD | LLM 集成 | ❌ |
