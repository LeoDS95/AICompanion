"""
AI Companion - Python Bridge
读取游戏状态 → 调用 AI 模型 → 写入指令文件
"""

import json
import time
import os
import sys
from pathlib import Path

# === 配置 ===
AI_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\ai"
STATE_FILE = os.path.join(AI_DIR, "state.json")
INSTRUCTION_FILE = os.path.join(AI_DIR, "instruction.json")

POLL_INTERVAL = 1.0  # 每秒检查一次状态

# === Prompt 模板 ===
SYSTEM_PROMPT = """你是星露谷物语的AI助手。你通过读取游戏状态JSON来了解当前情况，并发出指令控制角色。

## 你只能发出以下指令（JSON格式）：
- {"Action": "moveTo", "X": tile_x, "Y": tile_y}  — 移动到指定坐标
- {"Action": "interact", "X": tile_x, "Y": tile_y}  — 与物体交互
- {"Action": "useItem", "Slot": 0-11}  — 使用背包指定槽位的物品
- {"Action": "changeItem", "Slot": 0-11}  — 切换到指定槽位
- {"Action": "talkTo", "Npc": "名字"}  — 与NPC对话
- {"Action": "emote", "Text": "happy/sad/angry/love/surprise"}  — 显示表情
- {"Action": "say", "Text": "要说的话"}  — 聊天框说话
- {"Action": "wait", "DurationMs": 1000}  — 等待指定毫秒

## 规则：
1. 每次只返回一条指令的JSON，不要包含其他文字
2. 根据当前状态决定下一步行动
3. 注意体力(energy)，低体力时不要做消耗体力的事
4. 每天6:00起床，23:00前应该回家睡觉
5. 优先完成日常：浇水、照顾动物、采矿等
"""


def read_state():
    """读取游戏状态"""
    try:
        if not os.path.exists(STATE_FILE):
            return None
        with open(STATE_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    except (json.JSONDecodeError, IOError):
        return None


def write_instruction(instruction: dict):
    """写入指令文件（原子写入）"""
    tmp_file = INSTRUCTION_FILE + ".tmp"
    try:
        with open(tmp_file, "w", encoding="utf-8") as f:
            json.dump(instruction, f, indent=2, ensure_ascii=False)
        # 原子替换
        if os.path.exists(INSTRUCTION_FILE):
            os.remove(INSTRUCTION_FILE)
        os.rename(tmp_file, INSTRUCTION_FILE)
    except IOError as e:
        print(f"[错误] 写入指令失败: {e}")


def ask_ai(state: dict, history: list = None) -> dict:
    """
    调用 AI 模型获取下一步指令
    这里是占位实现，后续接入真实模型
    """
    # TODO: 接入真实模型 API
    # 目前返回一个简单的测试指令
    print(f"[AI] 当前状态: {state['PlayerName']} @ {state['LocationName']} "
          f"({state['PlayerX']:.0f},{state['PlayerY']:.0f}) "
          f"时间:{state['TimeString']} 体力:{state['Energy']}/{state['MaxEnergy']}")

    # 暂时用简单逻辑代替 AI
    return simple_ai_logic(state)


def simple_ai_logic(state: dict) -> dict:
    """
    简单 AI 逻辑（用于测试通信链路）
    后续替换为真实 LLM 调用
    """
    hour = state["TimeOfDay"] // 100
    energy = state["Energy"]
    location = state["LocationName"]

    # 体力低 → 等待
    if energy < 50:
        return {"Action": "wait", "DurationMs": 5000}

    # 晚上 → 回家（简化：直接瞬移回农舍附近）
    if hour >= 23:
        return {"Action": "moveTo", "X": 9, "Y": 9}

    # 默认：随机走动测试
    import random
    x = random.randint(5, 25)
    y = random.randint(5, 25)
    return {"Action": "moveTo", "X": x, "Y": y}


def main():
    print("=== AI Companion Bridge ===")
    print(f"监控目录: {AI_DIR}")
    print(f"状态文件: {STATE_FILE}")
    print(f"指令文件: {INSTRUCTION_FILE}")
    print()

    last_state_hash = None

    while True:
        try:
            state = read_state()

            if state is None:
                time.sleep(POLL_INTERVAL)
                continue

            # 检测状态是否变化
            state_hash = f"{state.get('PlayerX',0):.0f},{state.get('PlayerY',0):.0f},{state.get('TimeOfDay',0)}"
            if state_hash == last_state_hash:
                time.sleep(POLL_INTERVAL)
                continue

            last_state_hash = state_hash

            # 状态变化，请求 AI 指令
            # 注意：当前 state.json 每秒更新，但我们只在位置/时间变化时才决策
            if not state.get("WaitingForInstruction", False):
                time.sleep(POLL_INTERVAL)
                continue

            instruction = ask_ai(state)

            if instruction:
                print(f"[指令] {instruction}")
                write_instruction(instruction)

        except KeyboardInterrupt:
            print("\n[退出] AI Bridge 已停止")
            break
        except Exception as e:
            print(f"[错误] {e}")

        time.sleep(POLL_INTERVAL)


if __name__ == "__main__":
    main()
