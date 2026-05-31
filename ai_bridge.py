"""
AI Companion - Python Bridge v2
读取游戏状态 → 决策 → 写入指令文件

用法:
  python ai_bridge.py              # 使用简单逻辑测试
  python ai_bridge.py --llm        # 使用 LLM 决策（需要配置 API）
"""

import json
import time
import os
import sys
import random
from pathlib import Path
from datetime import datetime

# === 路径检测 ===
if os.path.exists("/mnt/c"):
    # WSL 环境
    AI_DIR = "/mnt/c/Program Files (x86)/Steam/steamapps/common/Stardew Valley/ai"
else:
    # Windows 环境
    AI_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\ai"

STATE_FILE = os.path.join(AI_DIR, "state.json")
INSTRUCTION_FILE = os.path.join(AI_DIR, "instruction.json")
PERSONA_FILE = os.path.join(AI_DIR, "persona.json")

POLL_INTERVAL = 0.5  # 每 0.5 秒检查一次


class GameConnection:
    """游戏连接器 - 读写 JSON 文件"""

    def __init__(self):
        os.makedirs(AI_DIR, exist_ok=True)

    def read_state(self) -> dict | None:
        """读取游戏状态"""
        try:
            if not os.path.exists(STATE_FILE):
                return None
            # 带重试的读取
            for _ in range(3):
                try:
                    with open(STATE_FILE, "r", encoding="utf-8") as f:
                        return json.load(f)
                except (json.JSONDecodeError, IOError):
                    time.sleep(0.05)
            return None
        except Exception:
            return None

    def write_instruction(self, instruction: dict) -> bool:
        """写入指令（原子写入）"""
        tmp_file = INSTRUCTION_FILE + ".tmp"
        try:
            with open(tmp_file, "w", encoding="utf-8") as f:
                json.dump(instruction, f, indent=2, ensure_ascii=False)
            # 原子替换
            if os.path.exists(INSTRUCTION_FILE):
                os.remove(INSTRUCTION_FILE)
            os.rename(tmp_file, INSTRUCTION_FILE)
            return True
        except IOError as e:
            print(f"[错误] 写入指令失败: {e}")
            return False

    def instruction_exists(self) -> bool:
        """检查是否有未执行的指令"""
        return os.path.exists(INSTRUCTION_FILE)

    def load_persona(self) -> dict:
        """加载人设配置"""
        try:
            if os.path.exists(PERSONA_FILE):
                with open(PERSONA_FILE, "r", encoding="utf-8") as f:
                    return json.load(f)
        except Exception:
            pass
        return {
            "CharacterName": "伙伴",
            "Personality": "勤劳、友善",
            "Role": "农场助手",
            "PreferredActivities": ["浇水", "收获", "喂动物"],
            "AvoidActivities": [],
            "SpeechStyle": "简洁自然"
        }


class SimpleAI:
    """简单 AI 逻辑（用于测试通信链路）"""

    def __init__(self):
        self.last_action = None
        self.idle_count = 0
        self.walk_target = None

    def decide(self, state: dict, persona: dict) -> dict:
        """根据状态决定下一步"""
        hour = state.get("TimeOfDay", 600) // 100
        minute = state.get("TimeOfDay", 600) % 100
        energy = state.get("Energy", 100)
        location = state.get("LocationName", "Unknown")
        x = state.get("PlayerX", 0)
        y = state.get("PlayerY", 0)

        # === 规则优先级 ===

        # 1. 体力过低 → 休息
        if energy < 30:
            return {"Action": "say", "Text": "好累...休息一下"}, {"Action": "wait", "DurationMs": 5000}

        # 2. 深夜 → 回农舍/小屋
        if hour >= 23 or hour < 6:
            if location not in ["FarmHouse", "Cabin"]:
                return {"Action": "say", "Text": "该睡觉了~"}, {"Action": "warpTo", "Npc": "FarmHouse", "X": 9, "Y": 9}
            return {"Action": "say", "Text": "晚安~"}, {"Action": "wait", "DurationMs": 10000}

        # 3. 在小屋/农舍 → 出门去农场
        if location in ["FarmHouse", "Cabin"]:
            return {"Action": "say", "Text": "早安！开工~"}, {"Action": "warpTo", "Npc": "Farm", "X": 48, "Y": 7}

        # 4. 在农场 → 随机走动测试
        if location == "Farm":
            # 随机选一个目标走动
            targets = [
                (48, 15, "去田里看看"),
                (35, 20, "去果树那边"),
                (60, 25, "去动物区"),
                (40, 10, "回农场入口"),
            ]
            target = random.choice(targets)
            return {"Action": "say", "Text": target[2]}, {"Action": "walkTo", "X": target[0], "Y": target[1]}

        # 5. 默认 → 等待
        return {"Action": "say", "Text": "这里好无聊~"}, {"Action": "wait", "DurationMs": 3000}


class LLMAI:
    """LLM 驱动的 AI（待实现）"""

    def __init__(self, api_key: str = None, model: str = "gpt-4"):
        self.api_key = api_key or os.environ.get("OPENAI_API_KEY")
        self.model = model
        self.history = []

    def decide(self, state: dict, persona: dict) -> dict:
        """调用 LLM 决策"""
        # TODO: 实现 LLM 调用
        # 暂时回退到简单逻辑
        print("[LLM] 尚未实现，使用简单逻辑")
        return SimpleAI().decide(state, persona)


class AIBridge:
    """主控制器"""

    def __init__(self, use_llm: bool = False):
        self.conn = GameConnection()
        self.ai = LLMAI() if use_llm else SimpleAI()
        self.persona = self.conn.load_persona()
        self.last_state = None
        self.running = True

    def log(self, msg: str, level: str = "INFO"):
        """带时间戳的日志"""
        ts = datetime.now().strftime("%H:%M:%S")
        print(f"[{ts}] [{level}] {msg}")

    def state_changed(self, old: dict, new: dict) -> bool:
        """检测状态是否有意义变化"""
        if old is None:
            return True
        # 比较关键字段
        keys = ["PlayerX", "PlayerY", "TimeOfDay", "LocationName", "Energy", "Health"]
        for k in keys:
            if old.get(k) != new.get(k):
                return True
        return False

    def run(self):
        """主循环"""
        self.log("=== AI Companion Bridge v2 ===")
        self.log(f"监控目录: {AI_DIR}")
        self.log(f"人设: {self.persona.get('CharacterName', '未知')}")
        self.log(f"AI 类型: {'LLM' if isinstance(self.ai, LLMAI) else '简单逻辑'}")
        self.log("等待游戏状态...")
        self.log("按 Ctrl+C 停止")
        print()

        check_count = 0
        while self.running:
            try:
                state = self.conn.read_state()

                # 没有状态文件 → 游戏未启动或 Mod 未加载
                if state is None:
                    check_count += 1
                    if check_count % 20 == 0:
                        self.log("等待游戏状态...")
                    time.sleep(POLL_INTERVAL)
                    continue

                # 状态没变化 → 跳过
                if not self.state_changed(self.last_state, state):
                    time.sleep(POLL_INTERVAL)
                    continue

                self.last_state = state.copy()
                check_count = 0

                # 有未执行的指令 → 等待执行完
                if self.conn.instruction_exists():
                    time.sleep(POLL_INTERVAL)
                    continue

                # 等待指令状态 → 可以下发新指令
                if not state.get("WaitingForInstruction", False):
                    time.sleep(POLL_INTERVAL)
                    continue

                # 决策
                say_text, instruction = self.ai.decide(state, self.persona)

                # 先说句话（如果有）
                if say_text:
                    self.conn.write_instruction(say_text)
                    time.sleep(0.5)  # 等待说话执行

                # 下发指令
                if instruction:
                    self.conn.write_instruction(instruction)
                    self.log(f"指令: {instruction.get('Action')} "
                            f"{instruction.get('X', '')} {instruction.get('Y', '')} "
                            f"{instruction.get('Npc', '')} {instruction.get('Text', '')}")

            except KeyboardInterrupt:
                self.log("停止中...")
                self.running = False
            except Exception as e:
                self.log(f"错误: {e}", "ERROR")
                time.sleep(1)

            time.sleep(POLL_INTERVAL)

        self.log("AI Bridge 已停止")


def main():
    import argparse
    parser = argparse.ArgumentParser(description="AI Companion Bridge")
    parser.add_argument("--llm", action="store_true", help="使用 LLM 决策")
    args = parser.parse_args()

    bridge = AIBridge(use_llm=args.llm)
    bridge.run()


if __name__ == "__main__":
    main()
