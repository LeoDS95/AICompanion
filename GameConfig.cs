using System.IO;

namespace AICompanion
{
    /// <summary>
    /// 通信路径配置
    /// </summary>
    public static class GameConfig
    {
        // 游戏根目录（SMAPI 启动时自动检测）
        public static string GameDir { get; private set; }
        public static string AIDir { get; private set; }
        public static string StateFile { get; private set; }
        public static string InstructionFile { get; private set; }

        public static void Init(string gameDir)
        {
            GameDir = gameDir;
            AIDir = Path.Combine(gameDir, "ai");
            StateFile = Path.Combine(AIDir, "state.json");
            InstructionFile = Path.Combine(AIDir, "instruction.json");

            // 确保目录存在
            Directory.CreateDirectory(AIDir);
        }
    }
}
