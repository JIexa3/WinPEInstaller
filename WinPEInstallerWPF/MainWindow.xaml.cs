using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPEInstallerWPF
{
    public partial class MainWindow : Window
    {
        private string usbDrivePath = "";

        public class SoftwareItem
        {
            public string Name { get; set; }
            public bool IsSelected { get; set; } = true;
            public System.Windows.Media.ImageSource Icon { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            FindUsbDrive();
            LoadWindowsImages();
            LoadAdditionalSoftware();
        }
        private System.Windows.Media.ImageSource GetIconFromFile(string filePath)
        {
            try
            {
                // Указываем точный путь к системному классу System.Drawing.Icon
                using (System.Drawing.Icon sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath))
                {
                    if (sysIcon != null)
                    {
                        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            sysIcon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch
            {
                // Если у файла поврежден заголовок или нет иконки
            }
            return null;
        }


        private void FindUsbDrive()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && Directory.Exists(Path.Combine(drive.Name, "Images")))
                {
                    usbDrivePath = drive.Name;
                    LblStatus.Text = $"Флешка подключена: {usbDrivePath}";
                    return;
                }
            }
            LblStatus.Text = "Ошибка: Каталог 'Images' не обнаружен.";
        }

        private void LoadWindowsImages()
        {
            string imagesPath = Path.Combine(usbDrivePath, "Images");
            if (Directory.Exists(imagesPath))
            {
                var files = Directory.GetFiles(imagesPath, "*.wim");
                foreach (var file in files)
                {
                    ComboImages.Items.Add(Path.GetFileName(file));
                }
                if (ComboImages.Items.Count > 0) ComboImages.SelectedIndex = 0;
            }
        }

        private void LoadAdditionalSoftware()
        {
            string softPath = Path.Combine(usbDrivePath, "Soft");
            var softwareList = new List<SoftwareItem>();

            if (Directory.Exists(softPath))
            {
                var apps = Directory.GetFiles(softPath, "*.exe");
                foreach (var app in apps)
                {
                    softwareList.Add(new SoftwareItem
                    {
                        Name = Path.GetFileName(app),
                        IsSelected = true,
                        Icon = GetIconFromFile(app)
                    });
                }
            }
            ListSoft.ItemsSource = softwareList;
        }


        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (ComboImages.SelectedItem == null)
            {
                MessageBox.Show("Укажите требуемый образ Windows!");
                return;
            }

            string selectedImage = Path.Combine(usbDrivePath, "Images", ComboImages.SelectedItem.ToString());
            var selectedApps = new List<string>();

            foreach (SoftwareItem item in ListSoft.ItemsSource)
            {
                if (item.IsSelected) selectedApps.Add(item.Name);
            }

            BtnStart.IsEnabled = false;
            ProgressInstall.IsIndeterminate = true;

            // Запускаем весь процесс асинхронно, чтобы UI-поток WPF не зависал
            await Task.Run(() => ExecuteDeployment(selectedImage, selectedApps));
        }

        private void ExecuteDeployment(string imagePath, List<string> apps)
        {
            UpdateStatus("Тест: создание сценария установки софта на диске E:\\...");

            string testScriptsDir = @"E:\Windows\Setup\Scripts";
            Directory.CreateDirectory(testScriptsDir);

            string cmdPath = Path.Combine(testScriptsDir, "SetupComplete.cmd");

            // Задаем правильный путь к debug.log прямо в папке E:\Windows\Setup\Scripts
            string logFile = Path.Combine(testScriptsDir, "debug.log");

            using (StreamWriter sw = File.CreateText(cmdPath))
            {
                sw.WriteLine("@echo off");
                sw.WriteLine("chcp 65001 > nul");

                // Проверка прав администратора
                sw.WriteLine("net session >nul 2>&1");
                sw.WriteLine("if %errorlevel% neq 0 (");
                sw.WriteLine("    echo [ОШИБКА] Запустите скрипт от имени Администратора!");
                sw.WriteLine("    pause");
                sw.WriteLine("    exit /b 1");
                sw.WriteLine(")");

                // 1. Инициализация debug.log на диске E:
                sw.WriteLine($"echo [%date% %time%] === ТЕСТОВЫЙ ПРОГОН (БЕЗ РЕАЛЬНОЙ УСТАНОВКИ) === > \"{logFile}\"");

                foreach (var appName in apps)
                {
                    string source = Path.Combine(usbDrivePath, "Soft", appName);
                    string dest = $@"C:\Windows\Temp\{appName}";

                    sw.WriteLine($"echo. >> \"{logFile}\"");
                    sw.WriteLine($"echo [%date% %time%] Тест копирования: \"{appName}\" >> \"{logFile}\"");

                    // Имитация копирования (проверка наличия файла на флешке)
                    sw.WriteLine($"if exist \"{source}\" (echo [OK] Файл найден на носителе >> \"{logFile}\") else (echo [FAIL] Исходный файл не найден! >> \"{logFile}\")");

                    string args = "/S";
                    if (appName.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                        args = "/silent /install";
                    else if (appName.Contains("VisualC", StringComparison.OrdinalIgnoreCase) || appName.Contains("vcredist", StringComparison.OrdinalIgnoreCase))
                        args = "/ai /gm2";
                    else if (appName.Contains("DirectX", StringComparison.OrdinalIgnoreCase))
                        args = "/Q /T:C:\\Windows\\Temp\\DX_Extract и запуск DXSETUP.exe /silent"; // Убрали символ '>'
                    else if (appName.Contains("vlc", StringComparison.OrdinalIgnoreCase))
                        args = "/L=1049 /S";
                    else if (appName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        args = "-s";

                    // Логируем имитацию вызова без опасных спецсимволов
                    sw.WriteLine($"echo [%date% %time%] [СИМУЛЯЦИЯ ЗАПУСКА] \"{dest}\" с аргументами: {args} >> \"{logFile}\"");
                    sw.WriteLine($"echo [%date% %time%] Программа \"{appName}\" успешно проверена >> \"{logFile}\"");
                }

                sw.WriteLine($"echo. >> \"{logFile}\"");
                sw.WriteLine($"echo [%date% %time%] === ТЕСТ ЗАВЕРШЕН, СОФТ НЕ ЗАТРОНУТ === >> \"{logFile}\"");
                sw.WriteLine($"echo Тестовый лог сформирован в {logFile}");
                sw.WriteLine("pause");
            }

            UpdateStatus("Тестовый скрипт без установки создан на E:\\!");
        }






        //private void ExecuteDeployment(string imagePath, List<string> apps)
        //{
        //    // 1. Форматирование диска через Diskpart
        //    UpdateStatus("Конфигурация целевого накопителя (Diskpart)...");
        //    string diskpartScript = Path.Combine(Path.GetTempPath(), "dp.txt");
        //    File.WriteAllText(diskpartScript, "select disk 0\nclean\nconvert gpt\ncreate partition efi size=100\nformat quick fs=fat32 label=\"System\"\nassign letter=S\ncreate partition primary\nformat quick fs=ntfs label=\"Windows\"\nassign letter=W\nexit");
        //    RunCommand("diskpart.exe", $"/s \"{diskpartScript}\"");

        //    // 2. Развертывание ОС через DISM
        //    UpdateStatus("Развертывание Windows 11 (DISM)... Это займет некоторое время.");
        //    RunCommand("dism.exe", $"/Apply-Image /ImageFile:\"{imagePath}\" /Index:1 /ApplyDir:W:\\");

        //    // 3. Генерация загрузчика UEFI
        //    UpdateStatus("Создание системного загрузчика BCD...");
        //    RunCommand("W:\\Windows\\System32\\bcdboot.exe", "W:\\Windows /s S: /f UEFI");

        //    // 4. Подготовка сценария автоматической установки софта
        //    UpdateStatus("Запись конфигурации SetupComplete.cmd...");
        //    string cmdPath = "W:\\Windows\\Setup\\Scripts\\SetupComplete.cmd";
        //    Directory.CreateDirectory(Path.GetDirectoryName(cmdPath));

        //    using (StreamWriter sw = File.CreateText(cmdPath))
        //    {
        //        sw.WriteLine("@echo off");
        //        foreach (var appName in apps)
        //        {
        //            string source = Path.Combine(usbDrivePath, "Soft", appName);
        //            sw.WriteLine($"copy /y \"{source}\" \"C:\\Windows\\Temp\\{appName}\"");
        //            sw.WriteLine($"start /wait C:\\Windows\\Temp\\{appName} /S /silent /quiet");
        //        }
        //    }

        //    UpdateStatus("Развертывание завершено! Перезагрузка ПК через 10 секунд...");
        //    System.Threading.Thread.Sleep(10000);
        //    RunCommand("wpeutil.exe", "reboot");
        //}

        private void RunCommand(string filename, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process proc = Process.Start(psi))
            {
                proc?.WaitForExit();
            }
        }

        private void UpdateStatus(string message)
        {
            // Безопасное обновление элементов UI из фонового потока Task
            Dispatcher.Invoke(() =>
            {
                LblStatus.Text = message;
            });
        }

        // Флаг для отслеживания текущей темы (по умолчанию стоит темная)
        private bool isDarkTheme = true;

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            // 1. Очищаем старые словари ресурсов из ресурсов приложения
            Application.Current.Resources.MergedDictionaries.Clear();

            // 2. Определяем имя файла новой темы в зависимости от текущего состояния
            string themeFile = isDarkTheme ? "LightTheme.xaml" : "DarkTheme.xaml";

            try
            {
                // 3. Создаем новый словарь ресурсов и подключаем его
                var newTheme = new ResourceDictionary
                {
                    Source = new Uri(themeFile, UriKind.RelativeOrAbsolute)
                };

                Application.Current.Resources.MergedDictionaries.Add(newTheme);

                // 4. Инвертируем флаг для следующего клика
                isDarkTheme = !isDarkTheme;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить файл темы '{themeFile}'. Убедитесь, что вы создали его в проекте.\nОшибка: {ex.Message}", "Ошибка тем оформления");
            }
        }

    }
}
