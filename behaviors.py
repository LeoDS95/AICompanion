"""
behaviors.py — 星露谷物语 AI Companion 行为参考手册 v2.0
=========================================================
这不是固定脚本，而是给 LLM 参考的行为指南。
LLM 可以根据这些参考行为自由发挥。

模块一：基础工作行为（浇水/收获/睡觉/天气响应/闲逛）
模块二：陪伴互动机制（跟随/打招呼/关心/庆祝/闲聊/反应）
模块三：事件提醒系统（节日/NPC生日提醒）
模块四：攻略检索机制（关键词检测 → search 指令）

所有移动均使用 walkTo，禁止传送。
输出格式：(say_dict, action_dict)
"""

from __future__ import annotations
import random
from typing import Optional

# ══════════════════════════════════════════════════════════════════════════════
# 一、游戏机制常量（给 LLM 参考）
# ══════════════════════════════════════════════════════════════════════════════

TIME_SYSTEM = {
    "day_start": 600,        # 06:00 AM
    "day_end_soft": 2200,    # 22:00 PM 建议回家
    "day_end_warning": 2400, # 00:00 AM 开始倒计时
    "day_end_hard": 2600,    # 02:00 AM 强制睡觉
    "bedtime_ideal": 2200,   # 理想就寝
}

ENERGY = {
    "initial_max": 270,
    "shake_warning": 20,     # 颤抖预警
    "exhausted": 0,          # 深度枯竭
    "pass_out": -15,         # 昏倒
    "rest_target": 50,       # 休息恢复目标
    "critical_low": 30,      # 低体力预警
}

CROPS_CALENDAR = {
    "spring": ["草莓", "土豆", "花椰菜", "防风草", "绿豆"],
    "summer": ["蓝莓", "甜瓜", "番茄", "辣椒", "啤酒花", "杨桃"],
    "fall": ["蔓越莓", "南瓜", "山药", "葡萄", "茄子"],
    "winter": [],
    "repeating": {
        "spring": ["草莓"],
        "summer": ["蓝莓", "啤酒花"],
        "fall": ["蔓越莓", "葡萄"],
    },
}

WEATHER_STRATEGY = {
    "晴天": {"water": True, "fish": False, "mine": True, "stay_home": False},
    "雨天": {"water": False, "fish": True, "mine": True, "stay_home": False},
    "暴风雨": {"water": False, "fish": False, "mine": True, "stay_home": True},
    "下雪": {"water": False, "fish": False, "mine": True, "stay_home": False},
}

LOCATIONS = {
    "farmhouse_door": (48, 7),
    "mailbox": (68, 16),
    "shipping_bin": (60, 15),
    "crop_area_center": (40, 30),
    "idle_spots": [(35, 25), (50, 20), (42, 35), (60, 28), (30, 18)],
}

ITEM_SLOTS = {
    "Axe": 0, "Hoe": 1, "Watering Can": 2,
    "Pickaxe": 3, "Scythe": 4,
}

FESTIVALS = {
    "spring": {13: "蛋节", 24: "花舞节"},
    "summer": {11: "月光水母之夜", 28: "夏季节"},
    "fall": {16: "星露谷展览会", 27: "万灵节"},
    "winter": {8: "冰雪节", 25: "冬日星盛宴"},
}

NPC_BIRTHDAYS = {
    "spring": {4: "刘易斯", 7: "文森特", 10: "哈维", 14: "潘姆", 18: "谢恩", 20: "皮埃尔", 26: "艾米丽"},
    "summer": {4: "亚历克斯", 8: "马龙", 10: "德米特里厄斯", 17: "格斯", 22: "威利", 26: "莉亚"},
    "fall": {2: "潘妮", 5: "艾利克斯", 8: "阿比盖尔", 11: "乔治", 15: "塞巴斯蒂安", 18: "莉亚", 24: "罗宾"},
    "winter": {1: "罗宾", 3: "科罗布斯", 7: "谢恩", 10: "海莉", 14: "马龙", 17: "艾利奥特", 20: "乔迪", 23: "肯特", 26: "德米特里厄斯"},
}

QUESTION_KEYWORDS = ["怎么", "如何", "什么", "哪里", "为什么", "哪个", "推荐", "怎样", "几个", "多少"]


# ══════════════════════════════════════════════════════════════════════════════
# 二、辅助函数
# ══════════════════════════════════════════════════════════════════════════════

def _say(text): return {"Action": "say", "Text": text}
def _walk(x, y): return {"Action": "walkTo", "X": x, "Y": y}
def _wait(ms=1000): return {"Action": "wait", "DurationMs": ms}
def _emote(text): return {"Action": "emote", "Text": text}
def _interact(x, y): return {"Action": "interact", "X": x, "Y": y}
def _use_item(slot): return {"Action": "useItem", "Slot": slot}
def _change_item(slot): return {"Action": "changeItem", "Slot": slot}
def _search(query): return {"Action": "search", "Query": query}

def _dist(state, x, y):
    sx, sy = state.get("PlayerX", 0), state.get("PlayerY", 0)
    return abs(sx - x) + abs(sy - y)

def _time_int(state): return state.get("TimeOfDay", 600)
def _energy(state): return state.get("Energy", 100)
def _weather(state): return state.get("Weather", "晴天")
def _season(state): return state.get("Season", "spring")
def _day(state): return state.get("Day", 1)
def _location(state): return state.get("LocationName", "Farm")


# ══════════════════════════════════════════════════════════════════════════════
# 三、模块一：基础工作行为
# ══════════════════════════════════════════════════════════════════════════════

def morning_routine(state, persona):
    """早起流程：说早安，看天气，规划今天"""
    weather = _weather(state)
    season = _season(state)
    day = _day(state)
    style = persona.get("style", "friendly")
    
    greetings = {
        "friendly": "早安！今天也要加油哦~",
        "lazy": "哈欠…早安…",
        "diligent": "早安！计划好了，开工！",
        "cheerful": "早安啊！！今天超期待的！",
    }
    greeting = greetings.get(style, "早安！")
    
    # 天气规划
    if weather == "晴天":
        plan = f"今天{weather}，先浇水再干其他活。"
    elif weather == "雨天":
        plan = f"今天{weather}，不用浇水，去钓鱼吧！"
    elif weather == "暴风雨":
        plan = f"今天{weather}，待在家里安全点。"
    else:
        plan = f"今天{weather}，继续干活！"
    
    return _say(f"{greeting} {season}第{day}天。{plan}"), _walk(48, 7)


def water_crops(state, persona):
    """浇水流程"""
    if _weather(state) == "雨天":
        return _say("下雨了，不用浇水~"), _emote("happy")
    if _energy(state) < 30:
        return rest_when_tired(state, persona)
    
    cx, cy = LOCATIONS["crop_area_center"]
    if _dist(state, cx, cy) > 25:
        return _say("去给作物浇水！"), _walk(cx, cy)
    return _say("开始浇水~"), _change_item(ITEM_SLOTS["Watering Can"])


def harvest_crops(state, persona):
    """收获流程"""
    if _energy(state) < 30:
        return rest_when_tired(state, persona)
    
    cx, cy = LOCATIONS["crop_area_center"]
    if _dist(state, cx, cy) > 25:
        return _say("去收获~"), _walk(cx, cy)
    return _say("收获时间~"), _change_item(ITEM_SLOTS["Scythe"])


def go_home_sleep(state, persona):
    """回家睡觉"""
    hx, hy = LOCATIONS["farmhouse_door"]
    time_str = state.get("TimeString", "22:00")
    
    if _dist(state, hx, hy) > 15:
        return _say(f"{time_str}了，回家吧~"), _walk(hx, hy)
    return _say(f"{time_str}了，晚安~"), _interact(hx, hy)


def rest_when_tired(state, persona):
    """休息"""
    energy = _energy(state)
    if energy <= 0:
        hx, hy = LOCATIONS["farmhouse_door"]
        return _say("体力没了！赶紧回家！"), _walk(hx, hy)
    return _say(f"累了…体力只剩{energy}，休息一下。"), _wait(3000)


def weather_response(state, persona):
    """天气响应"""
    weather = _weather(state)
    if weather == "暴风雨":
        return _say("暴风雨！待在家里！"), _emote("scared")
    elif weather == "雨天":
        return _say("下雨了，去钓鱼吧~"), _emote("happy")
    return _say(f"今天{weather}，继续干活！"), _emote("happy")


def idle_behavior(state, persona):
    """闲逛"""
    spot = random.choice(LOCATIONS["idle_spots"])
    musings = ["四处走走~", "农场风景真不错~", "没啥事，随便逛逛。"]
    return _say(random.choice(musings)), _walk(spot[0], spot[1])


# ══════════════════════════════════════════════════════════════════════════════
# 四、模块二：陪伴互动机制
# ══════════════════════════════════════════════════════════════════════════════

def companion_follow_player(state, persona):
    """跟随主人"""
    host_x = state.get("HostPlayerX")
    host_y = state.get("HostPlayerY")
    if host_x is None or host_y is None:
        return None
    
    cur_x = state.get("PlayerX", 0)
    cur_y = state.get("PlayerY", 0)
    dist = abs(cur_x - host_x) + abs(cur_y - host_y)
    
    if dist > 10:
        offset_x = random.randint(-3, 3)
        offset_y = random.randint(-2, 2)
        target_x = max(0, host_x + offset_x)
        target_y = max(0, host_y + offset_y)
        return _say("跟着你~"), _walk(target_x, target_y)
    return None


def companion_greeting(state, persona):
    """主动打招呼"""
    t = _time_int(state)
    style = persona.get("style", "friendly")
    
    if 600 <= t < 900:
        greets = {
            "friendly": "早安！今天也要一起加油哦~",
            "lazy": "哈欠…早安…",
            "diligent": "早安！计划好了吗？",
            "cheerful": "早安！！今天超期待的！！",
        }
        return _say(greets.get(style, "早安！")), _emote("happy")
    
    if 1100 <= t < 1300:
        return _say("中午了，记得吃饭哦~"), _emote("note")
    
    if 1800 <= t < 2100:
        return _say("天快黑了，准备收工吧~"), _emote("note")
    
    return None


def companion_comfort(state, persona):
    """关心主人"""
    energy = _energy(state)
    hour = _time_int(state)
    health = state.get("Health", 100)
    
    if energy < 50:
        return _say(f"体力只剩{energy}了，休息一下吧？"), _emote("love")
    
    if hour >= 2300:
        return _say("都这么晚了，早点睡吧~"), _emote("sad")
    
    if health < 50:
        return _say("你受伤了！小心点啊~"), _emote("shock")
    
    return None


def companion_celebrate(state, persona):
    """庆祝成就"""
    gold = state.get("Gold", 0)
    
    if gold > 0 and gold % 1000 < 50:
        return _say(f"哇，已经攒了{gold}金币了！"), _emote("happy")
    
    return None


def companion_chat(state, persona):
    """随机闲聊"""
    weather = _weather(state)
    season = _season(state)
    
    chats = []
    if weather == "晴天":
        chats.append("今天天气真好~")
    elif weather == "雨天":
        chats.append("下雨了，空气好清新~")
    
    if season == "spring":
        chats.append("春天来了！")
    elif season == "summer":
        chats.append("夏天好热啊~")
    
    chats.extend(["农场生活真惬意~", "有你一起玩真好~"])
    
    if chats:
        return _say(random.choice(chats)), _emote("note")
    return None


# ══════════════════════════════════════════════════════════════════════════════
# 五、模块三：事件提醒系统
# ══════════════════════════════════════════════════════════════════════════════

def check_festival_reminder(state, persona):
    """节日提醒"""
    season = _season(state).lower()
    day = _day(state)
    
    season_festivals = FESTIVALS.get(season, {})
    
    if day in season_festivals:
        name = season_festivals[day]
        return _say(f"今天是{name}！快去广场参加吧！"), _emote("happy")
    
    if (day + 1) in season_festivals:
        name = season_festivals[day + 1]
        return _say(f"明天是{name}，别忘了去参加哦！"), _emote("note")
    
    return None


def check_birthday_reminder(state, persona):
    """NPC生日提醒"""
    season = _season(state).lower()
    day = _day(state)
    
    season_birthdays = NPC_BIRTHDAYS.get(season, {})
    
    if day in season_birthdays:
        npc = season_birthdays[day]
        return _say(f"今天是{npc}的生日！记得送礼物！"), _emote("love")
    
    if (day + 1) in season_birthdays:
        npc = season_birthdays[day + 1]
        return _say(f"明天是{npc}的生日，记得准备礼物哦！"), _emote("note")
    
    return None


# ══════════════════════════════════════════════════════════════════════════════
# 六、模块四：攻略检索机制
# ══════════════════════════════════════════════════════════════════════════════

def handle_question(state, persona, player_message):
    """处理主人的问题"""
    if not player_message:
        return None
    
    if any(kw in player_message for kw in QUESTION_KEYWORDS):
        return _say(f"让我查一下「{player_message}」~"), _search(player_message)
    
    return None


def companion_ask_for_task(state, persona, player_message="", last_player_message_time=0):
    """
    主动问玩家任务
    
    逻辑：
    1. 刚开始游戏（第一次）→ 主动问
    2. 玩家说「没有/不用/随便」→ AI 自己决定
    3. 玩家长时间不说话（>5分钟）→ 再问一次
    4. 玩家还是不说 → AI 自由发挥
    """
    current_time = state.get("TimeOfDay", 600)
    
    # 玩家说「没有/不用/随便」→ AI 自己决定
    DECLINE_KEYWORDS = ["没有", "不用", "随便", "你自己玩", "没事", "不用管我"]
    if player_message and any(kw in player_message for kw in DECLINE_KEYWORDS):
        return _say("好的，那我自己安排啦~"), None  # 返回 None 让主决策自由发挥
    
    # 刚开始游戏（6:00-6:30）→ 主动问
    if 600 <= current_time <= 630:
        return _say("主人，今天有什么要我做的吗？"), None
    
    # 玩家长时间不说话（通过 last_player_message_time 判断）
    # 这个逻辑需要在主循环里实现
    
    return None


# ══════════════════════════════════════════════════════════════════════════════
# 七、主决策函数
# ══════════════════════════════════════════════════════════════════════════════

def decide(state, persona, player_message=""):
    """
    主决策入口。
    
    决策优先级：
    0. 主人发问 → 攻略检索
    1. 紧急情况（体力枯竭/深夜）→ 处理紧急
    2. 主动问任务（刚上线/长时间不说话）
    3. 陪伴行为（问候/关心/庆祝/闲聊）→ 主动互动
    4. 工作行为（浇水/收获/闲逛）→ 正常干活
    """
    # 0. 主人发问
    if player_message:
        result = handle_question(state, persona, player_message)
        if result: return result
    
    # 0.5 玩家说「没有/不用」→ AI 自己决定
    if player_message:
        result = companion_ask_for_task(state, persona, player_message)
        if result and result[0]: return result
    
    t = _time_int(state)
    energy = _energy(state)
    location = _location(state)
    
    # 1. 紧急情况
    if energy <= 0:
        return rest_when_tired(state, persona)
    
    if t >= 2400:
        return go_home_sleep(state, persona)
    
    if t >= 2200:
        return go_home_sleep(state, persona)
    
    if energy < 30:
        return rest_when_tired(state, persona)
    
    # 2. 事件提醒
    result = check_festival_reminder(state, persona)
    if result: return result
    
    result = check_birthday_reminder(state, persona)
    if result: return result
    
    # 3. 陪伴行为（30%概率）
    if random.random() < 0.3:
        result = companion_comfort(state, persona)
        if result: return result
        
        result = companion_greeting(state, persona)
        if result: return result
        
        result = companion_celebrate(state, persona)
        if result: return result
        
        result = companion_chat(state, persona)
        if result: return result
    
    # 4. 跟随主人
    if random.random() < 0.2:
        result = companion_follow_player(state, persona)
        if result and result[0]: return result
    
    # 5. 早晨流程
    if 600 <= t < 900:
        return morning_routine(state, persona)
    
    # 6. 天气响应
    weather = _weather(state)
    if weather == "暴风雨" and location == "Farm":
        return weather_response(state, persona)
    
    # 7. 工作行为
    if location == "Farm" and t < 1600:
        if random.random() < 0.4:
            return water_crops(state, persona)
    
    if location == "Farm" and t < 1800:
        if random.random() < 0.3:
            return harvest_crops(state, persona)
    
    # 8. 默认：闲逛
    return idle_behavior(state, persona)


# ══════════════════════════════════════════════════════════════════════════════
# 八、测试
# ══════════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    import json
    
    test_cases = [
        {"name": "🌅 春季早晨", "state": {
            "PlayerX": 576, "PlayerY": 608, "Energy": 270, "MaxEnergy": 270,
            "Gold": 500, "LocationName": "Farm", "Season": "spring", "Day": 3,
            "TimeOfDay": 800, "TimeString": "08:00", "Weather": "晴天",
            "Health": 100, "MaxHealth": 100,
            "InventorySummary": "Axex1, Hoex1, Watering Canx1, Pickaxex1, Scythex1",
        }},
        {"name": "🌧 下雨天", "state": {
            "PlayerX": 480, "PlayerY": 512, "Energy": 180, "MaxEnergy": 270,
            "Gold": 1200, "LocationName": "Farm", "Season": "summer", "Day": 7,
            "TimeOfDay": 1400, "TimeString": "14:00", "Weather": "雨天",
            "Health": 100, "MaxHealth": 100,
            "InventorySummary": "Axex1, Hoex1, Watering Canx1, Pickaxex1, Scythex1",
        }},
        {"name": "😰 体力低", "state": {
            "PlayerX": 400, "PlayerY": 450, "Energy": 12, "MaxEnergy": 270,
            "Gold": 750, "LocationName": "Farm", "Season": "fall", "Day": 15,
            "TimeOfDay": 1600, "TimeString": "16:00", "Weather": "晴天",
            "Health": 100, "MaxHealth": 100,
            "InventorySummary": "Axex1, Hoex1, Watering Canx1, Pickaxex1, Scythex1",
        }},
        {"name": "🌙 深夜", "state": {
            "PlayerX": 350, "PlayerY": 420, "Energy": 90, "MaxEnergy": 270,
            "Gold": 2000, "LocationName": "Farm", "Season": "spring", "Day": 20,
            "TimeOfDay": 2200, "TimeString": "22:00", "Weather": "晴天",
            "Health": 100, "MaxHealth": 100,
            "InventorySummary": "Axex1, Hoex1, Watering Canx1, Pickaxex1, Scythex1",
        }},
    ]
    
    persona = {"name": "活泼型", "style": "cheerful"}
    
    print("=" * 60)
    print(" AI Companion 行为测试（v2.0）")
    print("=" * 60)
    
    for test in test_cases:
        state = test["state"]
        say, action = decide(state, persona)
        print(f"\n📋 {test['name']}")
        print(f"   💬 {say['Text']}")
        print(f"   🎮 {json.dumps(action, ensure_ascii=False)}")
    
    print("\n" + "=" * 60)
    print(" 测试完成！")
    print("=" * 60)
