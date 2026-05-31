"""
AI Companion - 聊天控制测试
玩家通过聊天控制 AI 角色行动
"""

import json
import time
import os
from datetime import datetime

# === 路径检测 ===
if os.path.exists("/mnt/c"):
    AI_DIR = "/mnt/c/Program Files (x86)/Steam/steamapps/common/Stardew Valley/ai"
else:
    AI_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\ai"

CHAT_FILE = os.path.join(AI_DIR, "chat.json")
INSTRUCTION_FILE = os.path.join(AI_DIR, "instruction.json")
STATE_FILE = os.path.join(AI_DIR, "state.json")

POLL_INTERVAL = 0.5


def log(msg, level="INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    print(f"[{ts}] [{level}] {msg}")


def read_chat():
    """读取聊天消息"""
    try:
        if not os.path.exists(CHAT_FILE):
            return None
        with open(CHAT_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
        os.remove(CHAT_FILE)
        return data
    except (json.JSONDecodeError, IOError):
        return None


def read_state():
    """读取游戏状态"""
    try:
        if not os.path.exists(STATE_FILE):
            return None
        with open(STATE_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    except (json.JSONDecodeError, IOError):
        return None


def write_instruction(instruction: dict) -> bool:
    """写入指令（原子写入）"""
    tmp_file = INSTRUCTION_FILE + ".tmp"
    try:
        with open(tmp_file, "w", encoding="utf-8") as f:
            json.dump(instruction, f, indent=2, ensure_ascii=False)
        if os.path.exists(INSTRUCTION_FILE):
            os.remove(INSTRUCTION_FILE)
        os.rename(tmp_file, INSTRUCTION_FILE)
        return True
    except IOError as e:
        log(f"写入指令失败: {e}", "ERROR")
        return False


def instruction_exists():
    """检查是否有未执行的指令"""
    return os.path.exists(INSTRUCTION_FILE)


def wait_for_instruction():
    """等待上一条指令执行完"""
    while instruction_exists():
        time.sleep(0.2)


def parse_command(message, state):
    """
    解析玩家消息，转换成指令
    支持的命令:
    - 去/走到 + 地点 → warpTo 或 walkTo
    - 浇水/挖矿/砍树/钓鱼 → useItem
    - 说话 + 内容 → say
    - 等待/停 → wait
    - 跟着我/过来 → walkTo 到玩家位置
    """
    msg = message.strip()
    
    # 去/走到 + 地点
    if msg.startswith("去") or msg.startswith("走到"):
        target = msg[1:].strip() if msg.startswith("去") else msg[2:].strip()
        
        # 地点映射
        locations = {
            "农场": ("Farm", 48, 7),
            "农场入口": ("Farm", 48, 7),
            "田里": ("Farm", 48, 15),
            "动物区": ("Farm", 60, 25),
            "小屋": ("FarmHouse", 9, 9),
            "家": ("FarmHouse", 9, 9),
            "农舍": ("FarmHouse", 9, 9),
            "镇上": ("Town", 50, 50),
            "镇": ("Town", 50, 50),
            "矿洞": ("Mine", 10, 10),
            "矿": ("Mine", 10, 10),
        }
        
        if target in locations:
            loc, x, y = locations[target]
            return {"Action": "warpTo", "Npc": loc, "X": x, "Y": y}
        else:
            # 尝试解析坐标
            try:
                parts = target.replace(",", " ").split()
                if len(parts) == 2:
                    x, y = int(parts[0]), int(parts[1])
                    return {"Action": "walkTo", "X": x, "Y": y}
            except:
                pass
            
            return {"Action": "say", "Text": f"不知道 {target} 在哪里~"}
    
    # 跟着我/过来
    if msg in ["跟着我", "过来", "跟我来", "来"]:
        if state:
            # 获取玩家位置（需要从状态中读取）
            # 暂时回复说做不到
            return {"Action": "say", "Text": "我看不到你在哪里~告诉我坐标？"}
        return {"Action": "say", "Text": "我看不到你在哪里~"}
    
    # 浇水
    if msg in ["浇水", "浇地", "给作物浇水"]:
        return {"Action": "say", "Text": "好的，我去浇水~"}
        # TODO: 实际执行浇水动作
    
    # 挖矿
    if msg in ["挖矿", "去挖矿", "采矿"]:
        return {"Action": "warpTo", "Npc": "Mine", "X": 10, "Y": 10}
    
    # 砍树
    if msg in ["砍树", "砍木头", "伐木"]:
        return {"Action": "say", "Text": "好的，我去砍树~"}
        # TODO: 实际执行砍树动作
    
    # 钓鱼
    if msg in ["钓鱼", "去钓鱼"]:
        return {"Action": "say", "Text": "好的，我去钓鱼~"}
        # TODO: 实际执行钓鱼动作
    
    # 等待/停
    if msg in ["等待", "停", "停下", "等等"]:
        return {"Action": "wait", "DurationMs": 10000}
    
    # 说话
    if msg.startswith("说") or msg.startswith("喊"):
        content = msg[1:].strip()
        if content:
            return {"Action": "say", "Text": content}
    
    # 默认：当作普通聊天，AI 回复
    return {"Action": "say", "Text": f"你说的是：{msg}"}


def main():
    log("=== 聊天控制测试 ===")
    log(f"监控目录: {AI_DIR}")
    log("玩家可以通过聊天控制 AI 角色")
    log("支持的命令:")
    log("  去农场/去矿洞/去镇上 → 移动到指定地点")
    log("  跟着我/过来 → 走到玩家位置")
    log("  浇水/挖矿/砍树/钓鱼 → 执行动作")
    log("  等待/停 → 暂停行动")
    log("  说+内容 → 让 AI 说话")
    log("按 Ctrl+C 停止")
    print()

    last_processed_ts = 0
    my_replies = set()

    while True:
        try:
            # 读取游戏状态
            state = read_state()
            
            # 检查聊天消息
            chat = read_chat()
            if chat:
                sender = chat.get("Sender", "未知")
                message = chat.get("Message", "")
                timestamp = chat.get("Timestamp", 0)

                # 跳过已处理的消息
                if timestamp <= last_processed_ts:
                    time.sleep(POLL_INTERVAL)
                    continue

                # 跳过自己回复的消息
                if message in my_replies:
                    my_replies.discard(message)
                    last_processed_ts = timestamp
                    time.sleep(POLL_INTERVAL)
                    continue

                # 处理新消息
                last_processed_ts = timestamp
                log(f"收到消息: {sender}: {message}")

                # 等待上一条指令执行完
                wait_for_instruction()

                # 解析命令
                instruction = parse_command(message, state)
                log(f"解析指令: {instruction}")

                # 记录自己的回复（如果是 say 指令）
                if instruction.get("Action") == "say":
                    my_replies.add(instruction.get("Text", ""))

                # 发送指令
                if write_instruction(instruction):
                    log(f"已发送指令: {instruction}")
                else:
                    log("发送指令失败", "ERROR")

            time.sleep(POLL_INTERVAL)

        except KeyboardInterrupt:
            log("停止中...")
            break
        except Exception as e:
            log(f"错误: {e}", "ERROR")
            time.sleep(1)

    log("聊天控制测试已停止")


if __name__ == "__main__":
    main()
