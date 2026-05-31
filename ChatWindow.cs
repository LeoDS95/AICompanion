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
    /// AI 聊天窗口
    /// - T 键呼出输入框（屏幕底部）
    /// - AI 回复显示在屏幕上方（字幕样式，蓝色字，无背景）
    /// </summary>
    public class ChatWindow
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        
        // 消息历史（输入框用）
        private readonly List<ChatMessage> _messages = new();
        private const int MAX_MESSAGES = 50;
        
        // 字幕显示（屏幕上方）
        private string _currentSubtitle = "";
        private DateTime _subtitleTime;
        private const int SUBTITLE_DURATION_MS = 5000;  // 字幕显示 5 秒
        
        // 输入状态
        private string _inputText = "";
        private bool _isOpen = false;
        
        // 键盘状态
        private KeyboardState _currentKeyState;
        private HashSet<Keys> _pressedKeys = new();
        
        // UI 配置
        private const int INPUT_WIDTH = 600;
        private const int INPUT_HEIGHT = 30;
        private const int MARGIN = 10;
        
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
        /// 打开输入框
        /// </summary>
        public void Open()
        {
            _isOpen = true;
            _inputText = "";
            _monitor.Log("[ChatWindow] 打开", LogLevel.Info);
        }
        
        /// <summary>
        /// 关闭输入框
        /// </summary>
        public void Close()
        {
            _isOpen = false;
            _inputText = "";
            _monitor.Log("[ChatWindow] 关闭", LogLevel.Info);
        }
        
        /// <summary>
        /// 显示字幕（屏幕上方，蓝色字，无背景）
        /// </summary>
        public void ShowSubtitle(string text)
        {
            _currentSubtitle = text;
            _subtitleTime = DateTime.Now;
            _monitor.Log($"[ChatWindow] 字幕: {text}", LogLevel.Info);
        }
        
        /// <summary>
        /// 添加消息到历史
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
        }
        
        /// <summary>
        /// 每帧更新 - 处理键盘输入
        /// </summary>
        private void OnUpdateTicking(object sender, StardewModdingAPI.Events.UpdateTickingEventArgs e)
        {
            if (!_isOpen) return;
            
            _currentKeyState = Keyboard.GetState();
            var pressedKeys = _currentKeyState.GetPressedKeys();
            
            foreach (var key in pressedKeys)
            {
                if (!_pressedKeys.Contains(key))
                {
                    ProcessKeyPress(key);
                }
            }
            _pressedKeys = new HashSet<Keys>(pressedKeys);
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
        /// 处理按键事件 - 拦截 T 键
        /// </summary>
        private void OnButtonPressed(object sender, StardewModdingAPI.Events.ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            
            // T 键打开/关闭
            if (e.Button == SButton.T)
            {
                if (_isOpen)
                    Close();
                else
                    Open();
                
                _helper.Input.Suppress(e.Button);
                return;
            }
            
            // 输入框打开时，阻止所有按键传递给游戏
            if (_isOpen)
            {
                _helper.Input.Suppress(e.Button);
            }
        }
        
        /// <summary>
        /// 渲染
        /// </summary>
        private void OnRendered(object sender, StardewModdingAPI.Events.RenderedEventArgs e)
        {
            if (!Context.IsWorldReady) return;
            
            try
            {
                var spriteBatch = e.SpriteBatch;
                
                // 1. 渲染字幕（屏幕上方，蓝色字，无背景）
                RenderSubtitle(spriteBatch);
                
                // 2. 渲染输入框（屏幕底部，仅在打开时）
                if (_isOpen)
                {
                    RenderInputBox(spriteBatch);
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"[ChatWindow] 渲染失败: {ex.Message}", LogLevel.Debug);
            }
        }
        
        /// <summary>
        /// 渲染字幕（工具栏上方，蓝色字，无背景）
        /// </summary>
        private void RenderSubtitle(SpriteBatch spriteBatch)
        {
            if (string.IsNullOrEmpty(_currentSubtitle)) return;
            
            // 检查是否过期
            if ((DateTime.Now - _subtitleTime).TotalMilliseconds > SUBTITLE_DURATION_MS)
            {
                _currentSubtitle = "";
                return;
            }
            
            // 使用游戏小字体（和工具栏文字一样大）
            var font = Game1.smallFont;
            
            // 获取游戏 UI 缩放比例
            float uiScale = Game1.options.uiScale / 100f;
            
            // 最大宽度：屏幕宽度的 60%
            int maxWidth = (int)(Game1.viewport.Width * 0.6f);
            
            // 换行处理
            var lines = WrapText(font, _currentSubtitle, maxWidth, uiScale);
            
            // 工具栏在屏幕底部，高度约 60 像素（按缩放）
            float toolbarHeight = 60 * uiScale;
            float lineHeight = font.LineSpacing * uiScale * 1.3f;
            float totalHeight = lines.Count * lineHeight;
            
            // 字幕位置：工具栏上方
            float startY = Game1.viewport.Height - toolbarHeight - totalHeight - 20 * uiScale;
            float startX = (Game1.viewport.Width - maxWidth) / 2;  // 水平居中
            
            // 绘制每一行
            for (int i = 0; i < lines.Count; i++)
            {
                Vector2 position = new Vector2(startX, startY + i * lineHeight);
                // 绘制阴影（黑色）
                spriteBatch.DrawString(font, lines[i], position + new Vector2(1, 1), Color.Black, 0f, Vector2.Zero, uiScale, SpriteEffects.None, 0f);
                // 绘制文字（蓝色）
                spriteBatch.DrawString(font, lines[i], position, Color.Cyan, 0f, Vector2.Zero, uiScale, SpriteEffects.None, 0f);
            }
        }
        
        /// <summary>
        /// 文字换行
        /// </summary>
        private List<string> WrapText(SpriteFont font, string text, int maxWidth, float scale)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            var currentLine = "";
            
            foreach (var word in words)
            {
                var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                var size = font.MeasureString(testLine) * scale;
                
                if (size.X > maxWidth && !string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
                else
                {
                    currentLine = testLine;
                }
            }
            
            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine);
            
            return lines;
        }
        
        /// <summary>
        /// 渲染输入框（屏幕底部居中）
        /// </summary>
        private void RenderInputBox(SpriteBatch spriteBatch)
        {
            // 输入框位置
            Rectangle inputRect = new Rectangle(
                (Game1.viewport.Width - INPUT_WIDTH) / 2,
                Game1.viewport.Height - INPUT_HEIGHT - MARGIN,
                INPUT_WIDTH,
                INPUT_HEIGHT
            );
            
            // 背景（半透明黑色）
            spriteBatch.Draw(Game1.staminaRect, inputRect, new Color(0, 0, 0, 180));
            
            // 边框（黄色）
            DrawBorder(spriteBatch, inputRect, Color.Yellow);
            
            // 输入文本
            string displayText = _inputText;
            if (DateTime.Now.Millisecond % 1000 < 500)
                displayText += "|";
            
            Vector2 textPos = new Vector2(inputRect.X + 5, inputRect.Y + 7);
            spriteBatch.DrawString(Game1.smallFont, displayText, textPos, Color.White);
            
            // 提示文字
            if (string.IsNullOrEmpty(_inputText))
            {
                Vector2 hintPos = new Vector2(inputRect.X + 5, inputRect.Y + 7);
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
