"""
AI Companion - 聊天监听器
只监听消息并打印，AI 在 OpenClaw 里决策后直接写 instruction.json
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
STATE_FILE = os.path.join(AI_DIR, "state.json")

POLL_INTERVAL = 0.5


def log(msg, level="INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    print(f"[{ts}] [{level}] {msg}", flush=True)


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


def main():
    log("=== 聊天监听器 ===")
    log(f"监控目录: {AI_DIR}")
    log("等待玩家消息，AI 在 OpenClaw 中决策...")
    log("按 Ctrl+C 停止")
    print()

    last_processed_ts = 0

    while True:
        try:
            # 读取游戏状态
            state = read_state()
            if state:
                # 每 30 秒打印一次状态
                if int(time.time()) % 30 == 0:
                    log(f"[状态] {state.get('PlayerName')} @ {state.get('LocationName')} "
                        f"({state.get('PlayerX', 0):.0f},{state.get('PlayerY', 0):.0f}) "
                        f"时间:{state.get('TimeString')} 体力:{state.get('Energy')}")

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

                # 处理新消息
                last_processed_ts = timestamp
                log(f"[消息] {sender}: {message}")
                log(f"[上下文] 位置:{state.get('LocationName') if state else '未知'} "
                    f"时间:{state.get('TimeString') if state else '未知'} "
                    f"体力:{state.get('Energy') if state else '未知'}")

            time.sleep(POLL_INTERVAL)

        except KeyboardInterrupt:
            log("停止中...")
            break
        except Exception as e:
            log(f"[错误] {e}", "ERROR")
            time.sleep(1)

    log("聊天监听器已停止")


if __name__ == "__main__":
    main()
