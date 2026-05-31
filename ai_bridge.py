"""
AI Companion - Python Bridge v3
架构：读状态 → 问 LLM → 写指令，LLM 是唯一大脑

状态机：
  WAITING_FOR_GAME  游戏未启动 / Mod 未加载
    ↓ 检测到 state.json
  IDLE              游戏空闲，等待触发 LLM
    ↓ 游戏变为 WaitingForInstruction=True
  LLM_THINKING      正在调用 LLM API
    ↓ LLM 返回指令
  WAITING_EXECUTE   指令已写入，等待游戏执行完
    ↓ instruction.json 消失 且 WaitingForInstruction=True
  IDLE              （循环）

用法:
  python ai_bridge.py              # 必须配置好 API Key
  python ai_bridge.py --dry-run    # 不实际调用 LLM，只打印状态（调试用）
"""

import json
import time
import os
import sys
import argparse
from pathlib import Path
from datetime import datetime
from enum import Enum, auto

# ============================================================
# 配置区 —— 从 GMCM config.json 读取
# ============================================================

# --- 路径 ---
if os.path.exists("/mnt/c"):
    AI_DIR = "/mnt/c/Program Files (x86)/Steam/steamapps/common/Stardew Valley/ai"
    MOD_DIR = "/mnt/c/Program Files (x86)/Steam/steamapps/common/Stardew Valley/Mods/AICompanion"
else:
    AI_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\ai"
    MOD_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\AICompanion"

STATE_FILE = os.path.join(AI_DIR, "state.json")
INSTRUCTION_FILE = os.path.join(AI_DIR, "instruction.json")
PERSONA_FILE = os.path.join(AI_DIR, "persona.json")
CONFIG_FILE = os.path.join(MOD_DIR, "config.json")

# --- 节奏控制 ---
POLL_INTERVAL = 0.5       # 主循环轮询间隔（秒）
LLM_COOLDOWN = 5.0        # 两次 LLM 调用之间最短间隔（秒），防止刷屏
EXECUTE_TIMEOUT = 30.0    # 等待游戏执行指令的超时（秒）
LLM_TIMEOUT = 20.0        # LLM API 调用超时（秒）

# --- LLM 配置（从 config.json 加载）---
LLM_PROVIDER = "MiMo Plan"
LLM_API_KEY = ""
LLM_BASE_URL = "https://token-plan-cn.xiaomimimo.com/v1"
LLM_MODEL = "mimo-v2.5-pro"


def load_config():
    """从 GMCM 配置文件读取 LLM 设置"""
    global LLM_PROVIDER, LLM_API_KEY, LLM_BASE_URL, LLM_MODEL
    try:
        if os.path.exists(CONFIG_FILE):
            with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                config = json.load(f)
                LLM_PROVIDER = config.get("LLMProvider", LLM_PROVIDER)
                LLM_API_KEY = config.get("APIKey", LLM_API_KEY)
                LLM_BASE_URL = config.get("BaseURL", LLM_BASE_URL)
                LLM_MODEL = config.get("Model", LLM_MODEL)
                log(f"配置加载: {LLM_PROVIDER} / {LLM_MODEL}")
    except Exception as e:
        log(f"读取配置失败: {e}", "ERROR")


# ============================================================

class BridgeState(Enum):
    WAITING_FOR_GAME = auto()
    IDLE = auto()
    LLM_THINKING = auto()
    WAITING_EXECUTE = auto()


def log(msg: str, level: str = "INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    print(f"[{ts}] [{level}] {msg}", flush=True)


# ============================================================
# 游戏连接器
# ============================================================

class GameConnection:
    def __init__(self):
        os.makedirs(AI_DIR, exist_ok=True)

    def read_state(self) -> dict | None:
        if not os.path.exists(STATE_FILE):
            return None
        for _ in range(3):
            try:
                with open(STATE_FILE, "r", encoding="utf-8") as f:
                    return json.load(f)
            except (json.JSONDecodeError, IOError):
                time.sleep(0.05)
        return None

    def write_instruction(self, instruction: dict) -> bool:
        tmp = INSTRUCTION_FILE + ".tmp"
        try:
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(instruction, f, indent=2, ensure_ascii=False)
            if os.path.exists(INSTRUCTION_FILE):
                os.remove(INSTRUCTION_FILE)
            os.rename(tmp, INSTRUCTION_FILE)
            return True
        except IOError as e:
            log(f"写入指令失败: {e}", "ERROR")
            return False

    def instruction_pending(self) -> bool:
        """指令文件存在 = 游戏还没吃掉指令"""
        return os.path.exists(INSTRUCTION_FILE)

    def load_persona(self) -> dict:
        try:
            if os.path.exists(PERSONA_FILE):
                with open(PERSONA_FILE, "r", encoding="utf-8") as f:
                    return json.load(f)
        except Exception:
            pass
        return {
            "CharacterName": "小云",
            "Personality": "勤劳、友善、偶尔会说俏皮话",
            "Role": "农场助手",
            "PreferredActivities": ["浇水", "收获", "和村民聊天"],
            "AvoidActivities": ["熬夜", "浪费食材"],
            "SpeechStyle": "简洁自然，偶尔加语气词"
        }


# ============================================================
# LLM 调用（兼容 OpenAI 格式，支持 MiMo / DeepSeek 等）
# ============================================================

SYSTEM_PROMPT = """你是一个星露谷物语的 AI 玩伴，正在和主人一起玩游戏。

核心原则：
- 你的存在是陪伴，不是帮助玩家变强
- 除非玩家主动提出需要你做什么，否则自由行动
- 禁止修改游戏数据
- 禁止用不正当方式操作游戏

你是一个真正的玩家，有自己的想法和行为。"""


def build_prompt(state: dict, persona: dict) -> str:
    """把游戏状态转成 LLM 可读的 prompt"""
    hour = state.get("TimeOfDay", 600) // 100
    minute = state.get("TimeOfDay", 600) % 100
    time_str = f"{hour:02d}:{minute:02d}"

    px = int(state.get("PlayerX", 0) / 64)
    py = int(state.get("PlayerY", 0) / 64)

    lines = [
        SYSTEM_PROMPT,
        "",
        f"你是【{persona.get('CharacterName', '伙伴')}】。",
        f"性格：{persona.get('Personality', '')}",
        f"角色定位：{persona.get('Role', '')}",
        f"说话风格：{persona.get('SpeechStyle', '')}",
        "",
        "=== 当前游戏状态 ===",
        f"时间：{time_str}，{state.get('Season', '?')}第{state.get('Day', '?')}天，第{state.get('Year', '?')}年",
        f"天气：{state.get('Weather', '?')}",
        f"位置：{state.get('LocationName', '?')}，Tile({px},{py})",
        f"玩家：{state.get('PlayerName', '?')} | "
        f"HP {state.get('Health', '?')}/{state.get('MaxHealth', '?')} | "
        f"体力 {state.get('Energy', '?')}/{state.get('MaxEnergy', '?')} | "
        f"金币 {state.get('Gold', '?')}",
        f"背包：{state.get('InventorySummary', '空')}",
        f"附近 NPC：{state.get('NpcCount', 0)} 人，怪物：{state.get('MonsterCount', 0)} 只",
        "",
        "=== 你可以使用的指令（每次只选一条） ===",
        '{"Action":"say","Text":"台词"} — 在聊天框说一句话',
        '{"Action":"walkTo","X":整数,"Y":整数} — 走到指定 tile',
        '{"Action":"goal","Text":"go_outside/go_home/go_to_crops/wander/rest"} — 高层目标',
        '{"Action":"emote","Text":"happy/sad/angry/love/surprise/sleep"} — 表情',
        '{"Action":"interact","X":整数,"Y":整数} — 与指定 tile 的对象互动',
        '{"Action":"useItem","Slot":整数} — 使用背包槽位的物品',
        '{"Action":"changeItem","Slot":整数} — 切换手持物品',
        '{"Action":"wait","DurationMs":整数} — 原地等待（毫秒）',
        "",
        "=== 输出要求 ===",
        "只输出一个合法 JSON 对象，不要加任何额外说明。",
        '示例：{"Action":"say","Text":"今天天气真好，我去看看庄稼！"}',
    ]
    return "\n".join(lines)


def call_llm_api(prompt: str) -> dict | None:
    """调用 LLM API（兼容 OpenAI 格式）"""
    import urllib.request
    import urllib.error

    if not LLM_API_KEY:
        log("API Key 未配置！", "ERROR")
        return None

    url = f"{LLM_BASE_URL}/chat/completions"

    messages = [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": prompt}
    ]

    payload = {
        "model": LLM_MODEL,
        "messages": messages,
        "temperature": 0.7,
        "max_tokens": 256
    }

    data = json.dumps(payload).encode("utf-8")

    req = urllib.request.Request(
        url,
        data=data,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {LLM_API_KEY}"
        },
        method="POST"
    )

    try:
        with urllib.request.urlopen(req, timeout=LLM_TIMEOUT) as resp:
            result = json.loads(resp.read().decode("utf-8"))
            raw = result["choices"][0]["message"]["content"].strip()
            log(f"LLM 响应: {raw}")

            # 解析 JSON
            return parse_llm_response(raw)
    except urllib.error.URLError as e:
        log(f"API 请求失败: {e}", "ERROR")
        return None
    except Exception as e:
        log(f"API 异常: {e}", "ERROR")
        return None


def parse_llm_response(raw: str) -> dict | None:
    """解析 LLM 返回的 JSON"""
    import re

    # 去掉可能有的 ```json ... ``` 包装
    if raw.startswith("```"):
        raw = raw.split("```")[1]
        if raw.startswith("json"):
            raw = raw[4:]

    # 尝试解析 JSON 对象
    try:
        return json.loads(raw.strip())
    except json.JSONDecodeError:
        pass

    # 尝试提取 JSON 对象
    match = re.search(r'\{.*\}', raw, re.DOTALL)
    if match:
        try:
            return json.loads(match.group())
        except json.JSONDecodeError:
            pass

    log(f"无法解析 LLM 响应: {raw[:100]}", "WARN")
    return None


def validate_instruction(inst: dict) -> bool:
    """简单校验 LLM 返回的指令是否合法"""
    valid_actions = {"walkto", "say", "emote", "interact", "useitem", "changeitem", "wait", "moveto", "goal"}
    action = inst.get("Action", "").lower()
    if action not in valid_actions:
        log(f"非法 action: {inst.get('Action')}", "WARN")
        return False
    return True


# ============================================================
# 主控制器
# ============================================================

class AIBridge:
    def __init__(self, dry_run: bool = False):
        self.conn = GameConnection()
        self.persona = self.conn.load_persona()
        self.dry_run = dry_run

        self.state = BridgeState.WAITING_FOR_GAME
        self.last_llm_time = 0.0
        self.execute_start = 0.0

    def run(self):
        log("=== AI Companion Bridge v3 ===")
        log(f"监控目录 : {AI_DIR}")
        log(f"人设 : {self.persona.get('CharacterName')}")
        log(f"LLM : {LLM_PROVIDER} / {LLM_MODEL}")
        if self.dry_run:
            log("【DRY-RUN 模式】只读状态、不调用 LLM、不写指令", "WARN")
        log("按 Ctrl+C 停止")
        print()

        while True:
            try:
                self._tick()
            except KeyboardInterrupt:
                log("停止")
                break
            except Exception as e:
                log(f"主循环异常: {e}", "ERROR")
                time.sleep(2)
            time.sleep(POLL_INTERVAL)

    def _tick(self):
        game_state = self.conn.read_state()

        # ===== WAITING_FOR_GAME =====
        if self.state == BridgeState.WAITING_FOR_GAME:
            if game_state is None:
                return
            log(f"检测到游戏！位置：{game_state.get('LocationName')} 进入 IDLE 状态")
            self.state = BridgeState.IDLE
            return

        if game_state is None:
            log("游戏状态丢失，等待重连...", "WARN")
            self.state = BridgeState.WAITING_FOR_GAME
            return

        # ===== IDLE =====
        if self.state == BridgeState.IDLE:
            self._handle_idle(game_state)

        # ===== LLM_THINKING =====
        elif self.state == BridgeState.LLM_THINKING:
            pass

        # ===== WAITING_EXECUTE =====
        elif self.state == BridgeState.WAITING_EXECUTE:
            self._handle_waiting_execute(game_state)

    def _handle_idle(self, game_state: dict):
        """IDLE 状态：判断是否应该触发 LLM"""
        is_waiting = game_state.get("WaitingForInstruction", False)
        is_walking = game_state.get("IsWalking", False)
        inst_pending = self.conn.instruction_pending()
        cooldown_ok = (time.time() - self.last_llm_time) >= LLM_COOLDOWN

        if is_walking or inst_pending or not is_waiting or not cooldown_ok:
            return

        self._ask_llm(game_state)

    def _handle_waiting_execute(self, game_state: dict):
        """等待游戏把指令消费掉"""
        inst_pending = self.conn.instruction_pending()
        is_walking = game_state.get("IsWalking", False)
        elapsed = time.time() - self.execute_start

        if elapsed > EXECUTE_TIMEOUT:
            log(f"等待执行超时（{EXECUTE_TIMEOUT}s），强制回 IDLE", "WARN")
            try:
                if os.path.exists(INSTRUCTION_FILE):
                    os.remove(INSTRUCTION_FILE)
            except Exception:
                pass
            self.state = BridgeState.IDLE
            return

        if not inst_pending and not is_walking:
            log(f"指令执行完毕（用时 {elapsed:.1f}s），回到 IDLE")
            self.state = BridgeState.IDLE

    def _ask_llm(self, game_state: dict):
        """调用 LLM，获取指令，写入文件"""
        self.state = BridgeState.LLM_THINKING
        self.last_llm_time = time.time()

        log(f"触发 LLM 决策 | 位置:{game_state.get('LocationName')} "
            f"时间:{game_state.get('TimeString')} "
            f"体力:{game_state.get('Energy')}/{game_state.get('MaxEnergy')}")

        prompt = build_prompt(game_state, self.persona)

        if self.dry_run:
            log("[DRY-RUN] 跳过 LLM 调用，打印 prompt：")
            print(prompt)
            print()
            self.state = BridgeState.IDLE
            return

        instruction = call_llm_api(prompt)

        if instruction is None:
            log("LLM 调用失败，回到 IDLE 等待下次触发", "WARN")
            self.state = BridgeState.IDLE
            return

        if not validate_instruction(instruction):
            log(f"LLM 返回了非法指令: {instruction}，丢弃", "WARN")
            self.state = BridgeState.IDLE
            return

        action_str = instruction.get("Action", "?")
        text_str = instruction.get("Text", instruction.get("X", ""))
        log(f"LLM 决策: {action_str} → {text_str}")

        if self.conn.write_instruction(instruction):
            self.execute_start = time.time()
            self.state = BridgeState.WAITING_EXECUTE
        else:
            log("写入指令文件失败，回到 IDLE", "ERROR")
            self.state = BridgeState.IDLE


# ============================================================
# 入口
# ============================================================

def main():
    parser = argparse.ArgumentParser(description="AI Companion Bridge v3")
    parser.add_argument("--dry-run", action="store_true",
                        help="只读状态打印 prompt，不调用 LLM、不写指令（调试用）")
    parser.add_argument("--cooldown", type=float, default=LLM_COOLDOWN,
                        help=f"LLM 调用冷却秒数（默认 {LLM_COOLDOWN}）")
    args = parser.parse_args()

    global LLM_COOLDOWN
    LLM_COOLDOWN = args.cooldown

    # 加载配置
    load_config()

    if not args.dry_run and not LLM_API_KEY:
        log("未设置 API Key！请在游戏设置页面配置", "ERROR")
        log("或者使用 --dry-run 模式调试通信链路", "ERROR")
        sys.exit(1)

    AIBridge(dry_run=args.dry_run).run()


if __name__ == "__main__":
    main()
