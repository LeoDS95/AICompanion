"""
ai_bridge.py — AI Companion 控制脚本 v2.0
==========================================
功能：
- 读取游戏状态 (state.json)
- 读取玩家消息 (chat.json)
- 决策并执行指令 (instruction.json)
- Action Queue：AI 一次给多个指令，逐条执行
- 中断逻辑：危险时不打断，安全时问 LLM 判断

架构：
  游戏 ←→ ai_bridge.py ←→ LLM (OpenClaw)
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
REPLY_FILE = os.path.join(AI_DIR, "reply.json")

POLL_INTERVAL = 0.5
QUEUE_THRESHOLD = 0.8  # 执行完 80% 后问 AI 要新指令
INSTRUCTION_DELAY_MIN = 3.0  # 指令间最小延迟（秒）
INSTRUCTION_DELAY_MAX = 5.0  # 指令间最大延迟（秒）


# ══════════════════════════════════════════════════════════════════════════════
# Action Queue
# ══════════════════════════════════════════════════════════════════════════════

class ActionQueue:
    """动作队列：AI 一次给多个指令，逐条执行"""
    
    def __init__(self):
        self.queue = []
        self.executed = 0
        self.need_ask_ai = True  # 启动时需要问 AI 要第一批指令
        self._recent_keys = []  # 最近执行过的指令标识（去重用）
    
    def add_batch(self, instructions: list):
        """AI 一次给多个指令"""
        # 去重：过滤掉最近执行过的同类指令
        filtered = []
        for inst in instructions:
            # 生成指令的唯一标识（类型+目标）
            action = inst.get("Action")
            if action == "say":
                # say 指令：只保留一条，不重复
                if any(i.get("Action") == "say" for i in filtered):
                    continue
            elif action == "walkTo":
                # walkTo：相同目标不重复
                key = f"walkTo_{inst.get('X')}_{inst.get('Y')}"
                if key in self._recent_keys:
                    continue
                self._recent_keys.append(key)
            elif action == "emote":
                # emote：相同表情不重复
                key = f"emote_{inst.get('Text')}"
                if key in self._recent_keys:
                    continue
                self._recent_keys.append(key)
            
            filtered.append(inst)
            
            # 只保留最近 20 个 key
            if len(self._recent_keys) > 20:
                self._recent_keys.pop(0)
        
        self.queue = filtered
        self.executed = 0
        self.need_ask_ai = False
    
    def next(self) -> dict | None:
        """取下一条指令"""
        if not self.queue:
            return None
        
        instruction = self.queue.pop(0)
        self.executed += 1
        
        # 执行完 80% 后，标记需要问 AI
        total = self.executed + len(self.queue)
        if self.executed >= total * QUEUE_THRESHOLD:
            self.need_ask_ai = True
        
        return instruction
    
    @property
    def is_empty(self) -> bool:
        return len(self.queue) == 0
    
    @property
    def should_ask_ai(self) -> bool:
        """是否需要问 AI 要新指令"""
        return self.is_empty or self.need_ask_ai
    
    def clear(self):
        """清空队列（中断时用）"""
        self.queue.clear()
        self.executed = 0
        self.need_ask_ai = True


# ══════════════════════════════════════════════════════════════════════════════
# 中断逻辑
# ══════════════════════════════════════════════════════════════════════════════

INTERRUPT_KEYWORDS = ["停", "stop", "等一下", "别动", "过来", "帮我", "等等"]
CHAT_KEYWORDS = ["哈哈", "嗯", "好的", "知道", "谢谢", "晚安", "早安", "哦", "嗯嗯"]


def is_dangerous(state: dict) -> bool:
    """判断是否处于危险/紧急状态"""
    
    # 矿洞打怪
    if state.get("LocationName") == "Mine" and state.get("MonsterCount", 0) > 0:
        return True
    
    # 深夜（23:30 后）
    if state.get("TimeOfDay", 0) >= 2330:
        return True
    
    # 体力极低
    if state.get("Energy", 100) < 15:
        return True
    
    # 生命值低
    if state.get("Health", 100) < 30:
        return True
    
    return False


def should_interrupt(state: dict, player_message: str) -> tuple:
    """
    判断是否打断当前动作
    
    返回: (should_interrupt: bool, reason: str)
    - (True, "command") → 打断，执行新命令
    - (False, "chat") → 不打断，继续当前动作
    - (False, "dangerous") → 不打断，危险情况
    - (None, "ask_llm") → 不确定，问 LLM
    """
    
    # 1. 危险情况 → 不打断
    if is_dangerous(state):
        return False, "dangerous"
    
    # 2. 明确的打断指令
    if any(kw in player_message for kw in INTERRUPT_KEYWORDS):
        return True, "command"
    
    # 3. 明确的聊天
    if any(kw in player_message for kw in CHAT_KEYWORDS):
        return False, "chat"
    
    # 4. 不确定 → 问 LLM
    return None, "ask_llm"


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


def write_reply(text: str) -> bool:
    """写入回复（显示在主机聊天框）"""
    try:
        payload = {
            "text": text,
            "timestamp": int(time.time())
        }
        with open(REPLY_FILE, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
        return True
    except IOError as e:
        log(f"写入回复失败: {e}", "ERROR")
        return False


def instruction_exists() -> bool:
    """检查是否有未执行的指令"""
    return os.path.exists(INSTRUCTION_FILE)


# ══════════════════════════════════════════════════════════════════════════════
# 日志
# ══════════════════════════════════════════════════════════════════════════════

def log(msg: str, level: str = "INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    print(f"[{ts}] [{level}] {msg}", flush=True)


# ══════════════════════════════════════════════════════════════════════════════
# LLM 接口（占位，后续接入 OpenClaw）
# ══════════════════════════════════════════════════════════════════════════════

def ask_llm_for_action(state: dict, persona: dict, player_message: str = "") -> list:
    """
    问 LLM 要一批指令（10 条）
    
    返回: [{"Action": "say", "Text": "..."}, {"Action": "walkTo", "X": 48, "Y": 7}, ...]
    
    TODO: 接入 OpenClaw API
    """
    log("问 LLM 要指令（当前用简单逻辑代替）")
    
    from behaviors import decide
    
    # 一次生成 10 条指令
    batch = []
    for _ in range(5):  # 5 轮，每轮 say + action = 10 条
        say, action = decide(state, persona, player_message)
        if say:
            batch.append(say)
        if action:
            batch.append(action)
    
    return batch[:10]  # 最多 10 条
    
    return batch


def ask_llm_for_interrupt(state: dict, player_message: str) -> str:
    """
    问 LLM 判断是聊天还是命令
    
    返回: "chat" 或 "command"
    
    TODO: 接入 OpenClaw API
    """
    log(f"问 LLM 判断: '{player_message}'（当前用关键词代替）")
    
    # 简单判断：有问号或命令词 → command
    if "?" in player_message or "？" in player_message:
        return "command"
    
    command_words = ["去", "来", "做", "干", "帮我", "给我", "找", "买", "卖"]
    if any(w in player_message for w in command_words):
        return "command"
    
    return "chat"


# ══════════════════════════════════════════════════════════════════════════════
# 主循环
# ══════════════════════════════════════════════════════════════════════════════

def main():
    log("=== AI Companion Bridge v2.0 ===")
    log(f"监控目录: {AI_DIR}")
    log("按 Ctrl+C 停止")
    print()
    
    queue = ActionQueue()
    last_state = None
    last_chat_ts = 0
    
    while True:
        try:
            # ── 1. 读取状态 ─────────────────────────────────────────────
            state = read_state()
            if state is None:
                time.sleep(POLL_INTERVAL)
                continue
            
            # ── 2. 检查玩家消息 ─────────────────────────────────────────
            chat = read_chat()
            if chat:
                player_msg = chat.get("Message", "")
                chat_ts = chat.get("Timestamp", 0)
                
                if chat_ts > last_chat_ts:
                    last_chat_ts = chat_ts
                    log(f"[玩家] {player_msg}")
                    
                    # 判断是否打断
                    interrupt, reason = should_interrupt(state, player_msg)
                    
                    if interrupt is True:
                        # 明确打断
                        log(f"[中断] 打断当前动作，原因: {reason}")
                        queue.clear()
                        
                        # 问 LLM 要新指令
                        batch = ask_llm_for_action(state, {}, player_msg)
                        if batch:
                            queue.add_batch(batch)
                            log(f"[LLM] 收到 {len(batch)} 条指令")
                    
                    elif interrupt is False:
                        # 不打断
                        if reason == "dangerous":
                            log(f"[继续] 危险情况，不打断")
                            write_reply("等我忙完~")
                        else:
                            log(f"[继续] 聊天，不打断")
                    
                    else:
                        # 不确定 → 问 LLM
                        result = ask_llm_for_interrupt(state, player_msg)
                        
                        if result == "command":
                            log(f"[LLM] 判断为命令，打断")
                            queue.clear()
                            batch = ask_llm_for_action(state, {}, player_msg)
                            if batch:
                                queue.add_batch(batch)
                        else:
                            log(f"[LLM] 判断为聊天，继续")
            
            # ── 3. 执行队列指令 ─────────────────────────────────────────
            if not instruction_exists() and not queue.is_empty:
                instruction = queue.next()
                if instruction:
                    write_instruction(instruction)
                    log(f"[执行] {instruction.get('Action')} {instruction.get('Text', '')} {instruction.get('X', '')} {instruction.get('Y', '')}")
                    
                    # 随机延迟 3-5 秒（不像机器人）
                    delay = random.uniform(INSTRUCTION_DELAY_MIN, INSTRUCTION_DELAY_MAX)
                    time.sleep(delay)
            
            # ── 4. 队列空了 → 问 AI 要新指令 ──────────────────────────
            if queue.should_ask_ai and not instruction_exists():
                batch = ask_llm_for_action(state, {})
                if batch:
                    queue.add_batch(batch)
                    log(f"[LLM] 收到 {len(batch)} 条指令")
            
            # ── 5. 更新状态 ─────────────────────────────────────────────
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
