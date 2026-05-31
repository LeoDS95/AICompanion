"""
AI Companion - 聊天回显测试
玩家发什么，AI 回什么（测试完整链路）
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
        # 读取后删除，避免重复处理
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


def main():
    log("=== 聊天回显测试 ===")
    log(f"监控目录: {AI_DIR}")
    log("玩家发什么，AI 回什么")
    log("按 Ctrl+C 停止")
    print()

    last_processed_ts = 0  # 最后处理的消息时间戳
    my_replies = set()     # 记录自己回复的内容，避免回显

    while True:
        try:
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
                    my_replies.discard(message)  # 用完删除
                    last_processed_ts = timestamp
                    time.sleep(POLL_INTERVAL)
                    continue

                # 处理新消息
                last_processed_ts = timestamp
                log(f"收到消息: {sender}: {message}")

                # 等待上一条指令执行完
                while instruction_exists():
                    time.sleep(0.2)

                # 回显消息（用 say 指令）
                reply = f"你说的是：{message}"
                my_replies.add(reply)  # 记录自己的回复
                instruction = {"Action": "say", "Text": reply}
                
                if write_instruction(instruction):
                    log(f"已发送回复: {reply}")
                else:
                    log("发送回复失败", "ERROR")

            time.sleep(POLL_INTERVAL)

        except KeyboardInterrupt:
            log("停止中...")
            break
        except Exception as e:
            log(f"错误: {e}", "ERROR")
            time.sleep(1)

    log("聊天回显测试已停止")


if __name__ == "__main__":
    main()
