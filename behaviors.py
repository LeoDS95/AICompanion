"""
behaviors.py — 星露谷物语 AI Companion 行为参考手册 v4.0
=========================================================
只返回意图，不返回具体坐标。
Mod 决定怎么执行。

核心原则：
- Python 决定「做什么」（意图）
- C# 决定「怎么做」（执行）
"""

from __future__ import annotations
import random

# ══════════════════════════════════════════════════════════════════════════════
# 一、游戏机制常量
# ══════════════════════════════════════════════════════════════════════════════

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
# 三、AI 状态记忆（防止重复行为）
# ══════════════════════════════════════════════════════════════════════════════

class AIState:
    """AI 的记忆状态"""
    def __init__(self):
        self.today_greeted = False
        self.today_watered = False
        self.today_harvested = False
        self.last_ask_task_time = 0
        self.last_comfort_time = 0
        self.last_chat_time = 0
        self.current_season = None
        self.current_day = None
    
    def new_day(self, season, day):
        """新的一天，重置状态"""
        if self.current_season != season or self.current_day != day:
            self.today_greeted = False
            self.today_watered = False
            self.today_harvested = False
            self.last_ask_task_time = 0
            self.last_comfort_time = 0
            self.last_chat_time = 0
            self.current_season = season
            self.current_day = day

# 全局状态
_ai_state = AIState()


# ══════════════════════════════════════════════════════════════════════════════
# 四、意图函数（只返回意图，不返回坐标）
# ══════════════════════════════════════════════════════════════════════════════

def get_morning_intent(state, persona):
    """早起意图（只触发一次）"""
    if _ai_state.today_greeted:
        return None  # 已经问候过了
    
    _ai_state.today_greeted = True
    weather = _weather(state)
    season = _season(state)
    day = _day(state)
    
    return {
        "type": "morning_greeting",
        "intent": "早起问候",
        "context": f"{season}第{day}天，天气{weather}",
        "suggestion": "向主人问好，播报天气，询问今天计划",
        "goal": "go_outside"  # 意图：出门，不指定坐标
    }


def get_water_intent(state, persona):
    """浇水意图"""
    if _ai_state.today_watered:
        return None  # 今天已经浇过了
    
    if _weather(state) == "雨天":
        _ai_state.today_watered = True  # 下雨不用浇
        return {
            "type": "weather_check",
            "intent": "天气判断",
            "context": "下雨了，不用浇水",
            "suggestion": "告诉主人下雨了，可以做其他事",
            "goal": None
        }
    
    _ai_state.today_watered = True
    return {
        "type": "water_crops",
        "intent": "浇水",
        "context": "去田里给作物浇水",
        "suggestion": "告诉主人要去浇水",
        "goal": "go_to_crops"  # 意图：去作物区，不指定坐标
    }


def get_harvest_intent(state, persona):
    """收获意图"""
    if _ai_state.today_harvested:
        return None
    
    _ai_state.today_harvested = True
    return {
        "type": "harvest_crops",
        "intent": "收获",
        "context": "去田里收获成熟作物",
        "suggestion": "告诉主人要去收获",
        "goal": "go_to_crops"
    }


def get_sleep_intent(state, persona):
    """回家睡觉意图"""
    return {
        "type": "go_sleep",
        "intent": "回家睡觉",
        "context": f"现在{_time_int(state)}，该回家了",
        "suggestion": "向主人道晚安，回家睡觉",
        "goal": "go_home"  # 意图：回家，不指定坐标
    }


def get_rest_intent(state, persona):
    """休息意图"""
    return {
        "type": "rest",
        "intent": "休息",
        "context": f"体力只剩{_energy(state)}了",
        "suggestion": "告诉主人累了，需要休息",
        "goal": "rest"
    }


def get_idle_intent(state, persona):
    """闲逛意图"""
    return {
        "type": "idle",
        "intent": "闲逛",
        "context": "没事做，随便走走",
        "suggestion": "可以说说农场的风景，或者问问主人在做什么",
        "goal": "wander"  # 意图：闲逛，不指定坐标
    }


def get_comfort_intent(state, persona):
    """关心主人意图"""
    if _ai_state.last_comfort_time == _time_int(state):
        return None  # 同一时间不重复关心
    
    _ai_state.last_comfort_time = _time_int(state)
    return {
        "type": "comfort",
        "intent": "关心主人",
        "context": f"主人体力{_energy(state)}，时间{_time_int(state)}",
        "suggestion": "关心主人的身体，提醒休息或睡觉",
        "goal": None
    }


def get_ask_task_intent(state, persona):
    """主动问任务意图（只在早上问一次）"""
    if _ai_state.last_ask_task_time > 0:
        return None  # 已经问过了
    
    _ai_state.last_ask_task_time = _time_int(state)
    return {
        "type": "ask_task",
        "intent": "询问任务",
        "context": "不知道做什么，问主人",
        "suggestion": "主动问主人今天有什么要做的",
        "goal": None
    }


def get_decline_intent(state, persona):
    """玩家拒绝后，AI自己决定意图"""
    return {
        "type": "decline",
        "intent": "自主决定",
        "context": "主人说不用管，自己安排",
        "suggestion": "告诉主人自己安排，然后去做想做的事",
        "goal": "wander"  # 自己去闲逛
    }


def get_weather_intent(state, persona):
    """天气响应意图"""
    weather = _weather(state)
    return {
        "type": "weather_response",
        "intent": "天气响应",
        "context": f"今天{weather}",
        "suggestion": f"根据天气决定做什么（下雨不浇水，暴风雨待室内）",
        "goal": "stay_inside" if weather == "暴风雨" else None
    }


def get_festival_intent(state, persona, festival_name):
    """节日提醒意图"""
    return {
        "type": "festival_reminder",
        "intent": "节日提醒",
        "context": f"今天是{festival_name}",
        "suggestion": f"提醒主人今天是{festival_name}，邀请一起去参加",
        "goal": "go_to_town"  # 去镇上参加节日
    }


def get_birthday_intent(state, persona, npc_name):
    """生日提醒意图"""
    return {
        "type": "birthday_reminder",
        "intent": "生日提醒",
        "context": f"今天是{npc_name}的生日",
        "suggestion": f"提醒主人今天是{npc_name}的生日，建议送礼物",
        "goal": None
    }


def get_question_intent(state, persona, question):
    """问题检索意图"""
    return {
        "type": "question",
        "intent": "回答问题",
        "context": f"主人问了：{question}",
        "suggestion": "查找答案并回复主人",
        "goal": None
    }


# ══════════════════════════════════════════════════════════════════════════════
# 五、主决策函数（返回意图，不返回坐标）
# ══════════════════════════════════════════════════════════════════════════════

def decide(state, persona, player_message=""):
    """
    主决策入口。
    
    返回：意图（type + intent + context + suggestion + goal）
    Mod 根据 goal 决定怎么执行。
    """
    # 更新 AI 状态
    _ai_state.new_day(_season(state), _day(state))
    
    t = _time_int(state)
    energy = _energy(state)
    location = _location(state)
    
    # 0. 玩家发问
    if player_message:
        if any(kw in player_message for kw in QUESTION_KEYWORDS):
            return get_question_intent(state, persona, player_message)
        
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
    
    # 3. 早晨问候（只触发一次）
    if 600 <= t < 900:
        intent = get_morning_intent(state, persona)
        if intent:
            return intent
    
    # 4. 询问任务（只在早上问一次）
    if 600 <= t <= 630:
        intent = get_ask_task_intent(state, persona)
        if intent:
            return intent
    
    # 5. 天气响应
    weather = _weather(state)
    if weather == "暴风雨" and location == "Farm":
        return get_weather_intent(state, persona)
    
    # 6. 工作行为
    if location == "Farm" and t < 1600:
        if random.random() < 0.4:
            intent = get_water_intent(state, persona)
            if intent:
                return intent
    
    if location == "Farm" and t < 1800:
        if random.random() < 0.3:
            intent = get_harvest_intent(state, persona)
            if intent:
                return intent
    
    # 7. 陪伴行为（30% 概率）
    if random.random() < 0.3:
        intent = get_comfort_intent(state, persona)
        if intent:
            return intent
    
    # 8. 默认：闲逛
    return get_idle_intent(state, persona)


# ══════════════════════════════════════════════════════════════════════════════
# 六、测试
# ══════════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    import json
    
    test_cases = [
        {"name": "🌅 早起", "state": {
            "PlayerX": 576, "PlayerY": 608, "Energy": 270, "MaxEnergy": 270,
            "Gold": 500, "LocationName": "FarmHouse", "Season": "spring", "Day": 3,
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
    print(" AI Companion 行为意图测试（v4.0）")
    print("=" * 60)
    
    for test in test_cases:
        state = test["state"]
        message = test.get("message", "")
        intent = decide(state, persona, message)
        print(f"\n📋 {test['name']}")
        print(f"   类型: {intent['type']}")
        print(f"   意图: {intent['intent']}")
        print(f"   上下文: {intent['context']}")
        print(f"   建议: {intent['suggestion']}")
        print(f"   目标: {intent['goal']}")
    
    print("\n" + "=" * 60)
    print(" 测试完成！")
    print("=" * 60)
