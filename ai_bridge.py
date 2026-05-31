"""
ai_bridge.py — AI Companion 控制脚本 v5.0
==========================================
纯执行器：读状态 → 发给 LLM → 执行 LLM 的指令

架构：
  游戏 state.json → ai_bridge.py → LLM API → instruction.json → 游戏
"""

import json
import time
import os
import sys
import random
from pathlib import Path
from datetime import datetime

# ══════════════════════════════════════════════════════════════════════════════
# 配置
# ══════════════════════════════════════════════════════════════════════════════

if os.path.exists("/mnt/c"):
    AI_DIR = "/mnt/c/Program Files (x86)/Steam/steamapps/common/Stardew Valley/ai"
else:
    AI_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\ai"

STATE_FILE = os.path.join(AI_DIR, "state.json")
CHAT_FILE = os.path.join(AI_DIR, "chat.json")
INSTRUCTION_FILE = os.path.join(AI_DIR, "instruction.json")

POLL_INTERVAL = 2.0  # 每 2 秒检查一次状态变化
LLM_COOLDOWN = 5.0   # LLM 调用最小间隔（秒）


# ══════════════════════════════════════════════════════════════════════════════
# 日志
# ══════════════════════════════════════════════════════════════════════════════

def log(msg: str, level: str = "INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    print(f"[{ts}] [{level}] {msg}", flush=True)


# ══════════════════════════════════════════════════════════════════════════════
# 文件读写
# ══════════════════════════════════════════════════════════════════════════════

def read_state() -> dict | None:
    """读取游戏状态"""
    try:
        if not os.path.exists(STATE_FILE):
            return None
        for _ in range(3):
            try:
                with open(STATE_FILE, "r", encoding="utf-8") as f:
                    return json.load(f)
            except (json.JSONDecodeError, IOError):
                time.sleep(0.05)
        return None
    except Exception:
        return None


def read_chat() -> dict | None:
    """读取玩家消息"""
    try:
        if not os.path.exists(CHAT_FILE):
            return None
        with open(CHAT_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
        os.remove(CHAT_FILE)
        return data
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


def instruction_exists() -> bool:
    """检查是否有未执行的指令"""
    return os.path.exists(INSTRUCTION_FILE)


# ══════════════════════════════════════════════════════════════════════════════
# 状态变化检测
# ══════════════════════════════════════════════════════════════════════════════

def state_changed(old: dict | None, new: dict | None) -> bool:
    """检测状态是否有意义变化"""
    if old is None or new is None:
        return True
    keys = ["PlayerX", "PlayerY", "TimeOfDay", "LocationName", "Energy", "Health"]
    for k in keys:
        if old.get(k) != new.get(k):
            return True
    return False


# ══════════════════════════════════════════════════════════════════════════════
# LLM 接口（调用 OpenClaw 或其他 API）
# ══════════════════════════════════════════════════════════════════════════════

def ask_llm(state: dict, player_message: str = "") -> list:
    """
    问 LLM 该做什么。
    
    输入：游戏状态 + 玩家消息
    输出：指令列表 [{"Action": "say", "Text": "..."}, {"Action": "walkTo", "X": 48, "Y": 7}, ...]
    
    TODO: 接入真正的 LLM API
    """
    # 构建 prompt
    prompt = build_prompt(state, player_message)
    
    # TODO: 调用 LLM API
    # response = call_llm_api(prompt)
    # instructions = parse_llm_response(response)
    # return instructions
    
    # 临时：返回空列表（等 LLM 接入）
    log("等待 LLM 接入...", "DEBUG")
    return []


def build_prompt(state: dict, player_message: str = "") -> str:
    """构建 LLM prompt"""
    
    # 读取人设
    persona_file = os.path.join(AI_DIR, "persona.json")
    persona = {}
    if os.path.exists(persona_file):
        try:
            with open(persona_file, "r", encoding="utf-8") as f:
                persona = json.load(f)
        except:
            pass
    
    prompt = f"""你是一个星露谷物语的 AI 玩伴，正在和主人一起玩游戏。

## 你的身份
- 名字：{persona.get('name', '小助手')}
- 性格：{persona.get('personality', '友善、活泼')}
- 角色：{persona.get('role', '农场助手')}

## 当前游戏状态
- 时间：{state.get('TimeString', '?')}
- 季节：{state.get('Season', '?')} 第{state.get('Day', '?')}天
- 天气：{state.get('Weather', '?')}
- 位置：{state.get('LocationName', '?')}
- 坐标：({state.get('PlayerX', 0):.0f}, {state.get('PlayerY', 0):.0f})
- 体力：{state.get('Energy', 0)}/{state.get('MaxEnergy', 270)}
- 生命：{state.get('Health', 0)}/{state.get('MaxHealth', 100)}
- 金币：{state.get('Gold', 0)}
- 背包：{state.get('InventorySummary', '?')}
"""
    
    if player_message:
        prompt += f"\n## 主人刚刚说\n\"{player_message}\"\n"
    
    prompt += """
## 输出格式
返回 JSON 数组，每个元素是一条指令。可选指令：
- {"Action": "say", "Text": "说话内容"} — 在聊天框说话
- {"Action": "walkTo", "X": 数字, "Y": 数字} — 走到指定位置
- {"Action": "goal", "Text": "目标名"} — 执行高层目标（go_outside/go_home/go_to_crops/wander/rest）
- {"Action": "wait", "DurationMs": 数字} — 等待
- {"Action": "emote", "Text": "表情名"} — 显示表情（happy/sad/angry/love/note）

只返回 JSON 数组，不要其他文字。
"""
    
    return prompt


# ══════════════════════════════════════════════════════════════════════════════
# 主循环
# ══════════════════════════════════════════════════════════════════════════════

def main():
    log("=== AI Companion Bridge v5.0 ===")
    log(f"监控目录: {AI_DIR}")
    log("架构: 纯执行器，等 LLM 命令")
    log("按 Ctrl+C 停止")
    print()
    
    last_state = None
    last_chat_ts = 0
    last_llm_time = 0
    
    while True:
        try:
            # ── 1. 读取状态 ─────────────────────────────────────────────
            state = read_state()
            if state is None:
                time.sleep(POLL_INTERVAL)
                continue
            
            # ── 2. 检查状态变化 ─────────────────────────────────────────
            has_state_changed = state_changed(last_state, state)
            
            # ── 3. 检查玩家消息 ─────────────────────────────────────────
            chat = read_chat()
            player_message = ""
            if chat:
                player_msg = chat.get("Message", "")
                chat_ts = chat.get("Timestamp", 0)
                
                if chat_ts > last_chat_ts:
                    last_chat_ts = chat_ts
                    player_message = player_msg
                    log(f"[玩家] {player_msg}")
            
            # ── 4. 决定是否问 LLM ──────────────────────────────────────
            should_ask_llm = False
            
            # 有玩家消息 → 问 LLM
            if player_message:
                should_ask_llm = True
                log("[触发] 玩家消息")
            
            # 状态变化 + 没有正在执行的指令 + 冷却时间过了 → 问 LLM
            elif has_state_changed and not instruction_exists():
                now = time.time()
                if now - last_llm_time >= LLM_COOLDOWN:
                    should_ask_llm = True
                    log("[触发] 状态变化")
            
            # ── 5. 问 LLM ──────────────────────────────────────────────
            if should_ask_llm:
                log("[LLM] 询问中...")
                instructions = ask_llm(state, player_message)
                
                if instructions:
                    log(f"[LLM] 收到 {len(instructions)} 条指令")
                    
                    # 执行第一条指令
                    first = instructions[0]
                    if write_instruction(first):
                        log(f"[执行] {first.get('Action')} {first.get('Text', '')} {first.get('X', '')} {first.get('Y', '')}")
                        last_llm_time = time.time()
                    
                    # TODO: 如果有多条指令，放入队列逐条执行
                else:
                    log("[LLM] 无指令（等待中）")
            
            # ── 6. 更新状态 ─────────────────────────────────────────────
            last_state = state
            
            time.sleep(POLL_INTERVAL)
        
        except KeyboardInterrupt:
            log("停止中...")
            break
        except Exception as e:
            log(f"错误: {e}", "ERROR")
            time.sleep(1)
    
    log("AI Bridge 已停止")


if __name__ == "__main__":
    main()
