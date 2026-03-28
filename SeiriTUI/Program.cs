using Terminal.Gui;
using SeiriTUI.Views;

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
        }
    }
}
