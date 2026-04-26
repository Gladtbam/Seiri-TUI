using SeiriTUI.Views;
using Terminal.Gui;

namespace SeiriTUI
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;

            Application.Init();

            var mainWindow = new MainWindow();
            Application.Run(mainWindow);

            Application.Shutdown();

            // Terminal.Gui v1.x 退出后可能残留鼠标追踪模式，
            // 手动发送 ANSI 转义序列彻底关闭 SGR 扩展鼠标追踪，防止在 shell 中出现残留字符
            Console.Write("\x1b[?1006l"); // 关闭 SGR 扩展鼠标模式
            Console.Write("\x1b[?1003l"); // 关闭所有鼠标事件追踪
            Console.Write("\x1b[?1002l"); // 关闭按钮事件追踪
            Console.Write("\x1b[?1000l"); // 关闭基础鼠标追踪
            Console.Write("\x1b[?25h");   // 确保光标可见
        }
    }
}
