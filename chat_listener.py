"""
AI Companion - 聊天监听器 v2
监听玩家消息 → 打印到控制台 → AI 写 reply.json → 主机字幕显示
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
REPLY_FILE = os.path.join(AI_DIR, "reply.json")
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


def write_reply(text):
    """写入回复（主机 Mod 会读取并显示字幕）"""
    try:
        payload = {
            "text": text,
            "timestamp": int(time.time())
        }
        with open(REPLY_FILE, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
        log(f"[回复] 写入 reply.json: {text}")
        return True
    except Exception as e:
        log(f"[回复] 写入失败: {e}", "ERROR")
        return False


def main():
    log("=== AI Companion 聊天监听器 v2 ===")
    log(f"监控目录: {AI_DIR}")
    log("流程: 玩家说话 → 打印到控制台 → AI 写 reply.json → 主机字幕显示")
    log("按 Ctrl+C 停止")
    print()

    last_processed_ts = 0

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

                # 处理新消息
                last_processed_ts = timestamp
                log(f"[消息] {sender}: {message}")

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
