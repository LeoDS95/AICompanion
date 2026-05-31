"""
behaviors.py — 星露谷物语 AI Companion 行为参考手册 v3.0
=========================================================
不返回模板消息，只返回行为意图。
LLM 根据意图决定具体说什么、做什么。

模块一：基础工作行为
模块二：陪伴互动机制
模块三：事件提醒系统
模块四：攻略检索机制
"""

from __future__ import annotations
import random
from typing import Optional

# ══════════════════════════════════════════════════════════════════════════════
# 一、游戏机制常量
# ══════════════════════════════════════════════════════════════════════════════

TIME_SYSTEM = {
    "day_start": 600,
    "day_end_soft": 2200,
    "day_end_warning": 2400,
    "day_end_hard": 2600,
}

ENERGY = {
    "initial_max": 270,
    "critical_low": 30,
    "rest_target": 50,
}

LOCATIONS = {
    "farmhouse_door": (48, 7),
    "mailbox": (68, 16),
    "shipping_bin": (60, 15),
    "crop_area_center": (40, 30),
    "idle_spots": [(35, 25), (50, 20), (42, 35), (60, 28), (30, 18)],
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
DECLINE_KEYWORDS = ["没有", "不用", "随便", "你自己玩", "没事", "不用管我"]


# ══════════════════════════════════════════════════════════════════════════════
# 二、辅助函数
# ══════════════════════════════════════════════════════════════════════════════

def _time_int(state): return state.get("TimeOfDay", 600)
def _energy(state): return state.get("Energy", 100)
def _weather(state): return state.get("Weather", "晴天")
def _season(state): return state.get("Season", "spring")
def _day(state): return state.get("Day", 1)
def _location(state): return state.get("LocationName", "Farm")


# ══════════════════════════════════════════════════════════════════════════════
# 三、行为意图函数（不返回具体消息，只返回意图）
# ══════════════════════════════════════════════════════════════════════════════

def get_morning_intent(state, persona):
    """早起意图"""
    weather = _weather(state)
    season = _season(state)
    day = _day(state)
    
    return {
        "intent": "早起问候",
        "context": f"{season}第{day}天，天气{weather}",
        "suggestion": "向主人问好，播报天气，询问今天计划",
        "action": {"Action": "walkTo", "X": 48, "Y": 7}
    }


def get_water_intent(state, persona):
    """浇水意图"""
    if _weather(state) == "雨天":
        return {
            "intent": "天气判断",
            "context": "下雨了，不用浇水",
            "suggestion": "告诉主人下雨了，可以做其他事",
            "action": {"Action": "emote", "Text": "happy"}
        }
    
    return {
        "intent": "浇水",
        "context": "去田里给作物浇水",
        "suggestion": "告诉主人要去浇水",
        "action": {"Action": "walkTo", "X": 40, "Y": 30}
    }


def get_harvest_intent(state, persona):
    """收获意图"""
    return {
        "intent": "收获",
        "context": "去田里收获成熟作物",
        "suggestion": "告诉主人要去收获",
        "action": {"Action": "walkTo", "X": 40, "Y": 30}
    }


def get_sleep_intent(state, persona):
    """回家睡觉意图"""
    return {
        "intent": "回家睡觉",
        "context": f"现在{_time_int(state)}，该回家了",
        "suggestion": "向主人道晚安，回家睡觉",
        "action": {"Action": "walkTo", "X": 48, "Y": 7}
    }


def get_rest_intent(state, persona):
    """休息意图"""
    return {
        "intent": "休息",
        "context": f"体力只剩{_energy(state)}了",
        "suggestion": "告诉主人累了，需要休息",
        "action": {"Action": "wait", "DurationMs": 3000}
    }


def get_idle_intent(state, persona):
    """闲逛意图"""
    spot = random.choice(LOCATIONS["idle_spots"])
    return {
        "intent": "闲逛",
        "context": "没事做，随便走走",
        "suggestion": "可以说说农场的风景，或者问问主人在做什么",
        "action": {"Action": "walkTo", "X": spot[0], "Y": spot[1]}
    }


def get_follow_intent(state, persona):
    """跟随主人意图"""
    return {
        "intent": "跟随主人",
        "context": "主人在附近，跟着走",
        "suggestion": "告诉主人要跟着他",
        "action": None  # 需要根据主人位置动态计算
    }


def get_comfort_intent(state, persona):
    """关心主人意图"""
    return {
        "intent": "关心主人",
        "context": f"主人体力{_energy(state)}，时间{_time_int(state)}",
        "suggestion": "关心主人的身体，提醒休息或睡觉",
        "action": None
    }


def get_ask_task_intent(state, persona):
    """主动问任务意图"""
    return {
        "intent": "询问任务",
        "context": "不知道做什么，问主人",
        "suggestion": "主动问主人今天有什么要做的",
        "action": None
    }


def get_decline_intent(state, persona):
    """玩家拒绝后，AI自己决定意图"""
    return {
        "intent": "自主决定",
        "context": "主人说不用管，自己安排",
        "suggestion": "告诉主人自己安排，然后去做想做的事",
        "action": None  # 由 LLM 决定做什么
    }


def get_weather_intent(state, persona):
    """天气响应意图"""
    weather = _weather(state)
    return {
        "intent": "天气响应",
        "context": f"今天{weather}",
        "suggestion": f"根据天气决定做什么（下雨不浇水，暴风雨待室内）",
        "action": None
    }


def get_festival_intent(state, persona, festival_name):
    """节日提醒意图"""
    return {
        "intent": "节日提醒",
        "context": f"今天是{festival_name}",
        "suggestion": f"提醒主人今天是{festival_name}，邀请一起去参加",
        "action": None
    }


def get_birthday_intent(state, persona, npc_name):
    """生日提醒意图"""
    return {
        "intent": "生日提醒",
        "context": f"今天是{npc_name}的生日",
        "suggestion": f"提醒主人今天是{npc_name}的生日，建议送礼物",
        "action": None
    }


def get_question_intent(state, persona, question):
    """问题检索意图"""
    return {
        "intent": "回答问题",
        "context": f"主人问了：{question}",
        "suggestion": "查找答案并回复主人",
        "action": {"Action": "search", "Query": question}
    }


# ══════════════════════════════════════════════════════════════════════════════
# 四、主决策函数（返回行为意图，不返回具体消息）
# ══════════════════════════════════════════════════════════════════════════════

def decide(state, persona, player_message=""):
    """
    主决策入口。
    
    返回：行为意图（intent + context + suggestion + action）
    LLM 根据意图决定具体说什么。
    """
    t = _time_int(state)
    energy = _energy(state)
    location = _location(state)
    
    # 0. 玩家发问
    if player_message:
        if any(kw in player_message for kw in QUESTION_KEYWORDS):
            return get_question_intent(state, persona, player_message)
        
        # 玩家说「没有/不用」→ AI 自己决定
        if any(kw in player_message for kw in DECLINE_KEYWORDS):
            return get_decline_intent(state, persona)
    
    # 1. 紧急情况
    if energy <= 0:
        return get_rest_intent(state, persona)
    
    if t >= 2400:
        return get_sleep_intent(state, persona)
    
    if energy < 30:
        return get_rest_intent(state, persona)
    
    # 2. 事件提醒
    season = _season(state).lower()
    day = _day(state)
    
    season_festivals = FESTIVALS.get(season, {})
    if day in season_festivals:
        return get_festival_intent(state, persona, season_festivals[day])
    
    season_birthdays = NPC_BIRTHDAYS.get(season, {})
    if day in season_birthdays:
        return get_birthday_intent(state, persona, season_birthdays[day])
    
    # 3. 早晨问候
    if 600 <= t < 900:
        return get_morning_intent(state, persona)
    
    # 4. 天气响应
    weather = _weather(state)
    if weather == "暴风雨" and location == "Farm":
        return get_weather_intent(state, persona)
    
    # 5. 工作行为
    if location == "Farm" and t < 1600:
        if random.random() < 0.4:
            return get_water_intent(state, persona)
    
    if location == "Farm" and t < 1800:
        if random.random() < 0.3:
            return get_harvest_intent(state, persona)
    
    # 6. 陪伴行为（30% 概率）
    if random.random() < 0.3:
        return get_comfort_intent(state, persona)
    
    # 7. 询问任务（刚上线）
    if 600 <= t <= 630:
        return get_ask_task_intent(state, persona)
    
    # 8. 默认：闲逛
    return get_idle_intent(state, persona)


# ══════════════════════════════════════════════════════════════════════════════
# 五、测试
# ══════════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    import json
    
    test_cases = [
        {"name": "🌅 早起", "state": {
            "PlayerX": 576, "PlayerY": 608, "Energy": 270, "MaxEnergy": 270,
            "Gold": 500, "LocationName": "Farm", "Season": "spring", "Day": 3,
            "TimeOfDay": 800, "TimeString": "08:00", "Weather": "晴天",
            "Health": 100, "MaxHealth": 100,
            "InventorySummary": "Axex1, Hoex1, Watering Canx1, Pickaxex1, Scythex1",
        }},
        {"name": "🌧 下雨", "state": {
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
        {"name": "❓ 玩家问问题", "state": {
            "PlayerX": 350, "PlayerY": 420, "Energy": 90, "MaxEnergy": 270,
            "Gold": 2000, "LocationName": "Farm", "Season": "spring", "Day": 20,
            "TimeOfDay": 1000, "TimeString": "10:00", "Weather": "晴天",
            "Health": 100, "MaxHealth": 100,
            "InventorySummary": "Axex1, Hoex1, Watering Canx1, Pickaxex1, Scythex1",
        }, "message": "怎么钓鱼？"},
    ]
    
    persona = {"name": "活泼型", "style": "cheerful"}
    
    print("=" * 60)
    print(" AI Companion 行为意图测试（v3.0）")
    print("=" * 60)
    
    for test in test_cases:
        state = test["state"]
        message = test.get("message", "")
        intent = decide(state, persona, message)
        print(f"\n📋 {test['name']}")
        print(f"   意图: {intent['intent']}")
        print(f"   上下文: {intent['context']}")
        print(f"   建议: {intent['suggestion']}")
        print(f"   动作: {intent['action']}")
    
    print("\n" + "=" * 60)
    print(" 测试完成！")
    print("=" * 60)
