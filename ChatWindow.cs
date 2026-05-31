using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;

namespace AICompanion
{
    /// <summary>
    /// AI 聊天窗口 - 替换游戏原生聊天框
    /// T 键呼出，直接对接大模型
    /// </summary>
    public class ChatWindow
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        
        // 消息历史
        private readonly List<ChatMessage> _messages = new();
        private const int MAX_MESSAGES = 50;
        
        // 输入状态
        private string _inputText = "";
        private bool _isOpen = false;
        
        // 键盘状态
        private KeyboardState _currentKeyState;
        private KeyboardState _lastKeyState;
        private HashSet<Keys> _pressedKeys = new();
        
        // UI 配置
        private const int CHAT_WIDTH = 600;
        private const int CHAT_HEIGHT = 200;
        private const int INPUT_HEIGHT = 30;
        private const int MARGIN = 10;
        
        // 位置
        private Rectangle _windowRect;
        private Rectangle _inputRect;
        
        // 状态
        public bool IsOpen => _isOpen;
        
        // 事件：发送消息
        public event Action<string> OnMessageSent;
        
        public ChatWindow(IMonitor monitor, IModHelper helper)
        {
            _monitor = monitor;
            _helper = helper;
            
            // 注册事件
            helper.Events.Display.Rendered += OnRendered;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.GameLoop.UpdateTicking += OnUpdateTicking;
            
            _monitor.Log("[ChatWindow] 初始化完成，按 T 键打开聊天", LogLevel.Info);
        }
        
        /// <summary>
        /// 打开聊天窗口
        /// </summary>
        public void Open()
        {
            _isOpen = true;
            _inputText = "";
            _monitor.Log("[ChatWindow] 打开", LogLevel.Info);
        }
        
        /// <summary>
        /// 关闭聊天窗口
        /// </summary>
        public void Close()
        {
            _isOpen = false;
            _inputText = "";
            _monitor.Log("[ChatWindow] 关闭", LogLevel.Info);
        }
        
        /// <summary>
        /// 添加消息
        /// </summary>
        public void AddMessage(string sender, string text, Color color)
        {
            _messages.Add(new ChatMessage
            {
                Sender = sender,
                Text = text,
                Color = color,
                Timestamp = DateTime.Now
            });
            
            if (_messages.Count > MAX_MESSAGES)
                _messages.RemoveAt(0);
            
            _monitor.Log($"[ChatWindow] {sender}: {text}", LogLevel.Info);
        }
        
        /// <summary>
        /// 每帧更新 - 处理键盘输入
        /// </summary>
        private void OnUpdateTicking(object sender, StardewModdingAPI.Events.UpdateTickingEventArgs e)
        {
            if (!_isOpen) return;
            
            // 获取键盘状态
            _currentKeyState = Keyboard.GetState();
            
            // 检测新按下的键
            var pressedKeys = _currentKeyState.GetPressedKeys();
            foreach (var key in pressedKeys)
            {
                if (!_pressedKeys.Contains(key))
                {
                    // 新按下的键
                    ProcessKeyPress(key);
                }
            }
            _pressedKeys = new HashSet<Keys>(pressedKeys);
            
            _lastKeyState = _currentKeyState;
        }
        
        /// <summary>
        /// 处理按键
        /// </summary>
        private void ProcessKeyPress(Keys key)
        {
            // Enter 键发送消息
            if (key == Keys.Enter)
            {
                if (!string.IsNullOrEmpty(_inputText.Trim()))
                {
                    string message = _inputText.Trim();
                    AddMessage("主人", message, Color.White);
                    OnMessageSent?.Invoke(message);
                    _inputText = "";
                }
                return;
            }
            
            // Escape 键关闭
            if (key == Keys.Escape)
            {
                Close();
                return;
            }
            
            // Backspace 键删除字符
            if (key == Keys.Back && _inputText.Length > 0)
            {
                _inputText = _inputText.Substring(0, _inputText.Length - 1);
                return;
            }
            
            // 空格键
            if (key == Keys.Space)
            {
                _inputText += " ";
                return;
            }
            
            // 字母键 (A-Z)
            if (key >= Keys.A && key <= Keys.Z)
            {
                // 检查是否按下了 Shift
                bool shift = _currentKeyState.IsKeyDown(Keys.LeftShift) || _currentKeyState.IsKeyDown(Keys.RightShift);
                char c = shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
                _inputText += c;
                return;
            }
            
            // 数字键 (0-9)
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                _inputText += (char)('0' + (key - Keys.D0));
                return;
            }
            
            // 限制长度
            if (_inputText.Length > 50)
                _inputText = _inputText.Substring(0, 50);
        }
        
        /// <summary>
        /// 处理按键事件 - 拦截 T 键和其他按键
        /// </summary>
        private void OnButtonPressed(object sender, StardewModdingAPI.Events.ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            
            // T 键打开/关闭聊天窗口
            if (e.Button == SButton.T)
            {
                if (_isOpen)
                    Close();
                else
                    Open();
                
                _helper.Input.Suppress(e.Button);
                return;
            }
            
            // 聊天窗口打开时，阻止所有按键传递给游戏
            if (_isOpen)
            {
                _helper.Input.Suppress(e.Button);
            }
        }
        
        /// <summary>
        /// 渲染窗口
        /// </summary>
        private void OnRendered(object sender, StardewModdingAPI.Events.RenderedEventArgs e)
        {
            if (!_isOpen || !Context.IsWorldReady) return;
            
            try
            {
                var spriteBatch = e.SpriteBatch;
                
                // 更新位置（屏幕底部）
                _windowRect = new Rectangle(
                    Game1.viewport.Width / 2 - CHAT_WIDTH / 2,
                    Game1.viewport.Height - CHAT_HEIGHT - INPUT_HEIGHT - MARGIN,
                    CHAT_WIDTH,
                    CHAT_HEIGHT + INPUT_HEIGHT
                );
                
                _inputRect = new Rectangle(
                    _windowRect.X + MARGIN,
                    _windowRect.Y + CHAT_HEIGHT + MARGIN / 2,
                    _windowRect.Width - MARGIN * 2,
                    INPUT_HEIGHT - MARGIN
                );
                
                // 绘制背景（半透明黑色）
                spriteBatch.Draw(Game1.staminaRect, _windowRect, new Color(0, 0, 0, 200));
                
                // 绘制边框
                DrawBorder(spriteBatch, _windowRect, Color.White);
                
                // 绘制消息列表
                int messageY = _windowRect.Y + MARGIN;
                int maxMessages = (CHAT_HEIGHT - MARGIN * 2) / 18;
                int startIndex = Math.Max(0, _messages.Count - maxMessages);
                
                for (int i = startIndex; i < _messages.Count; i++)
                {
                    var msg = _messages[i];
                    string displayText = $"{msg.Sender}: {msg.Text}";
                    
                    if (displayText.Length > 50)
                        displayText = displayText.Substring(0, 47) + "...";
                    
                    Vector2 textPos = new Vector2(_windowRect.X + MARGIN, messageY);
                    spriteBatch.DrawString(Game1.smallFont, displayText, textPos, msg.Color);
                    messageY += 18;
                }
                
                // 绘制输入框
                DrawInputBox(spriteBatch);
            }
            catch (Exception ex)
            {
                _monitor.Log($"[ChatWindow] 渲染失败: {ex.Message}", LogLevel.Debug);
            }
        }
        
        /// <summary>
        /// 绘制输入框
        /// </summary>
        private void DrawInputBox(SpriteBatch spriteBatch)
        {
            // 输入框背景
            spriteBatch.Draw(Game1.staminaRect, _inputRect, new Color(50, 50, 50, 220));
            
            // 输入框边框
            DrawBorder(spriteBatch, _inputRect, Color.Yellow);
            
            // 输入文本
            string displayText = _inputText;
            if (DateTime.Now.Millisecond % 1000 < 500)
                displayText += "|";
            
            Vector2 textPos = new Vector2(_inputRect.X + 5, _inputRect.Y + 7);
            spriteBatch.DrawString(Game1.smallFont, displayText, textPos, Color.White);
            
            // 提示文字
            if (string.IsNullOrEmpty(_inputText))
            {
                Vector2 hintPos = new Vector2(_inputRect.X + 5, _inputRect.Y + 7);
                spriteBatch.DrawString(Game1.smallFont, "输入消息...", hintPos, Color.Gray);
            }
        }
        
        /// <summary>
        /// 绘制边框
        /// </summary>
        private void DrawBorder(SpriteBatch spriteBatch, Rectangle rect, Color color)
        {
            int thickness = 2;
            spriteBatch.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            spriteBatch.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y + rect.Height - thickness, rect.Width, thickness), color);
            spriteBatch.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            spriteBatch.Draw(Game1.staminaRect, new Rectangle(rect.X + rect.Width - thickness, rect.Y, thickness, rect.Height), color);
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            _helper.Events.Display.Rendered -= OnRendered;
            _helper.Events.Input.ButtonPressed -= OnButtonPressed;
            _helper.Events.GameLoop.UpdateTicking -= OnUpdateTicking;
        }
        
        /// <summary>
        /// 消息结构
        /// </summary>
        private class ChatMessage
        {
            public string Sender { get; set; }
            public string Text { get; set; }
            public Color Color { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
