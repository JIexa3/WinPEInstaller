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
        public class WindowsEditionItem
        {
            public string DisplayName { get; set; }
            public string FilePath { get; set; }
            public int Index { get; set; }

            public override string ToString() => DisplayName;
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
            if (!Directory.Exists(imagesPath)) return;

            ComboImages.Items.Clear();

            // 1. Проверяем образ Windows 11
            string win11Path = Path.Combine(imagesPath, "Win11.wim");
            if (File.Exists(win11Path))
            {
                ComboImages.Items.Add(new WindowsEditionItem
                {
                    DisplayName = "Windows 11 Профессиональная (Pro)",
                    FilePath = win11Path,
                    Index = 4 // Индекс Pro из вашего скриншота DISM
                });

                ComboImages.Items.Add(new WindowsEditionItem
                {
                    DisplayName = "Windows 11 Домашняя (Home)",
                    FilePath = win11Path,
                    Index = 1 // Индекс Home из вашего скриншота DISM
                });

                ComboImages.Items.Add(new WindowsEditionItem
                {
                    DisplayName = "Windows 11 Домашняя для одного языка",
                    FilePath = win11Path,
                    Index = 2
                });

                ComboImages.Items.Add(new WindowsEditionItem
                {
                    DisplayName = "Windows 11 Для образовательных учреждений",
                    FilePath = win11Path,
                    Index = 3
                });
            }

            // 2. Проверяем образ Windows 10
            string win10Path = Path.Combine(imagesPath, "Win10.wim");
            if (File.Exists(win10Path))
            {
                ComboImages.Items.Add(new WindowsEditionItem
                {
                    DisplayName = "Windows 10 Профессиональная (Pro)",
                    FilePath = win10Path,
                    Index = 2 // Индекс Pro для Win10
                });

                ComboImages.Items.Add(new WindowsEditionItem
                {
                    DisplayName = "Windows 10 Домашняя (Home)",
                    FilePath = win10Path,
                    Index = 1 // Индекс Home для Win10
                });
            }

            // Если файлы называются иначе, добавляем универсальное чтение
            if (ComboImages.Items.Count == 0)
            {
                var files = Directory.GetFiles(imagesPath, "*.wim");
                foreach (var file in files)
                {
                    ComboImages.Items.Add(new WindowsEditionItem
                    {
                        DisplayName = $"{Path.GetFileNameWithoutExtension(file)} (Индекс 1)",
                        FilePath = file,
                        Index = 1
                    });
                }
            }

            if (ComboImages.Items.Count > 0)
                ComboImages.SelectedIndex = 0;
        }

        private List<WindowsEditionItem> GetWimEditions(string wimPath)
        {
            var list = new List<WindowsEditionItem>();

            try
            {
                // Команда PowerShell для получения индекса и имени через разделитель "###"
                string psCommand = $"Get-WindowsImage -ImagePath '{wimPath}' | ForEach-Object {{ \"$($_.ImageIndex)###$($_.ImageName)\" }}";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using (Process proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    using (StringReader reader = new StringReader(output))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (string.IsNullOrWhiteSpace(line) || !line.Contains("###"))
                                continue;

                            var parts = line.Split(new string[] { "###" }, StringSplitOptions.None);
                            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int index))
                            {
                                string rawName = parts[1].Trim();
                                string friendlyName = FormatEditionName(rawName, Path.GetFileName(wimPath));

                                list.Add(new WindowsEditionItem
                                {
                                    DisplayName = friendlyName,
                                    FilePath = wimPath,
                                    Index = index
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка PowerShell: {ex.Message}");
            }

            return list;
        }



        private string FormatEditionName(string originalName, string fileName)
        {
            string name = originalName ?? string.Empty;
            string file = fileName ?? string.Empty;

            // 1. Автоматически определяем поколение ОС (Windows 10 или Windows 11)
            string osPrefix = "Windows 11";
            if (name.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("win10", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("10", StringComparison.OrdinalIgnoreCase))
            {
                osPrefix = "Windows 10";
            }

            // 2. Формируем понятные и удобные названия редакций
            if (name.Contains("Pro", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Workstation", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Education", StringComparison.OrdinalIgnoreCase))
            {
                return $"{osPrefix} Профессиональная (Pro)";
            }

            if (name.Contains("Домашняя для одного языка", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Single Language", StringComparison.OrdinalIgnoreCase))
            {
                return $"{osPrefix} Домашняя (Для одного языка)";
            }

            if (name.Contains("Домашняя", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Home", StringComparison.OrdinalIgnoreCase))
            {
                return $"{osPrefix} Домашняя (Home)";
            }

            if (name.Contains("Корпоративная", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
            {
                return $"{osPrefix} Корпоративная (Enterprise)";
            }

            if (name.Contains("образовательных", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Education", StringComparison.OrdinalIgnoreCase))
            {
                return $"{osPrefix} Для образовательных учреждений (Education)";
            }

            if (name.Contains("рабочих станций", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Workstations", StringComparison.OrdinalIgnoreCase))
            {
                return $"{osPrefix} Pro для рабочих станций (Workstations)";
            }

            // Если редакция нестандартная, возвращаем оригинальное системное имя
            return name;
        }





        //private void LoadWindowsImages()
        //{
        //    string imagesPath = Path.Combine(usbDrivePath, "Images");
        //    if (Directory.Exists(imagesPath))
        //    {
        //        var files = Directory.GetFiles(imagesPath, "*.wim");
        //        foreach (var file in files)
        //        {
        //            ComboImages.Items.Add(Path.GetFileName(file));
        //        }
        //        if (ComboImages.Items.Count > 0) ComboImages.SelectedIndex = 0;
        //    }
        //}

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
                MessageBox.Show("Выберите редакцию Windows!");
                return;
            }

            // Получаем выбранную редакцию с ее точным индексом
            var selectedEdition = (WindowsEditionItem)ComboImages.SelectedItem;

            var selectedApps = new List<string>();
            if (ListSoft.ItemsSource != null)
            {
                foreach (SoftwareItem item in ListSoft.ItemsSource)
                {
                    if (item.IsSelected) selectedApps.Add(item.Name);
                }
            }

            BtnStart.IsEnabled = false;
            ProgressInstall.IsIndeterminate = true;

            // Передаем путь и индекс (1 для Домашней, 4 для Pro и т.д.)
            await Task.Run(() => ExecuteDeployment(selectedEdition.FilePath, selectedEdition.Index, selectedApps));
        }



        private void ExecuteDeployment(string imagePath, int imageIndex, List<string> apps)
        {
            UpdateStatus("Конфигурация целевого накопителя (Diskpart)...");
            string diskpartScript = Path.Combine(Path.GetTempPath(), "dp.txt");
            File.WriteAllText(diskpartScript, "select disk 0\nclean\nconvert gpt\ncreate partition efi size=100\nformat quick fs=fat32 label=\"System\"\nassign letter=S\ncreate partition primary\nformat quick fs=ntfs label=\"Windows\"\nassign letter=W\nexit");
            RunCommand("diskpart.exe", $"/s \"{diskpartScript}\"");

            UpdateStatus($"Развертывание редакции (Индекс {imageIndex})... Это займет 5-8 минут.");
            RunCommand("dism.exe", $"/Apply-Image /ImageFile:\"{imagePath}\" /Index:{imageIndex} /ApplyDir:W:\\");

            UpdateStatus("Создание системного загрузчика BCD...");
            RunCommand("W:\\Windows\\System32\\bcdboot.exe", "W:\\Windows /s S: /f UEFI");

            // 4. Формирование боевого сценария установки софта
            UpdateStatus("Запись сценария SetupComplete.cmd...");
            string cmdPath = @"W:\Windows\Setup\Scripts\SetupComplete.cmd";
            Directory.CreateDirectory(Path.GetDirectoryName(cmdPath));

            using (StreamWriter sw = File.CreateText(cmdPath))
            {
                sw.WriteLine("@echo off");
                sw.WriteLine("chcp 65001 > nul");

                string logFile = @"C:\Windows\Temp\install.log";
                sw.WriteLine($"echo [%date% %time%] === СТАРТ УСТАНОВКИ ПО === > \"{logFile}\"");

                foreach (var appName in apps)
                {
                    string source = Path.Combine(usbDrivePath, "Soft", appName);
                    string dest = $@"C:\Windows\Temp\{appName}";

                    sw.WriteLine($"echo. >> \"{logFile}\"");
                    sw.WriteLine($"echo [%date% %time%] Копирование {appName}... >> \"{logFile}\"");
                    sw.WriteLine($"copy /y \"{source}\" \"{dest}\" >> \"{logFile}\" 2>&1");

                    // Индивидуальная обработка DirectX
                    if (appName.Contains("DirectX", StringComparison.OrdinalIgnoreCase))
                    {
                        sw.WriteLine($"echo [%date% %time%] Распаковка DirectX... >> \"{logFile}\"");
                        sw.WriteLine("mkdir C:\\Windows\\Temp\\DX_Extract > nul 2>&1");
                        sw.WriteLine($"start \"\" /wait \"{dest}\" /Q /T:C:\\Windows\\Temp\\DX_Extract");
                        sw.WriteLine($"echo [%date% %time%] Установка DXSETUP.exe... >> \"{logFile}\"");
                        sw.WriteLine("start \"\" /wait \"C:\\Windows\\Temp\\DX_Extract\\DXSETUP.exe\" /silent");
                        sw.WriteLine($"echo [%date% %time%] DirectX завершен [Код: %errorlevel%] >> \"{logFile}\"");
                        sw.WriteLine("rmdir /s /q C:\\Windows\\Temp\\DX_Extract > nul 2>&1");
                        continue;
                    }

                    // Ключи тихой установки
                    string args = "/S";
                    if (appName.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                        args = "/silent /install";
                    else if (appName.Contains("VisualC", StringComparison.OrdinalIgnoreCase) || appName.Contains("vcredist", StringComparison.OrdinalIgnoreCase))
                        args = "/ai /gm2";
                    else if (appName.Contains("vlc", StringComparison.OrdinalIgnoreCase))
                        args = "/L=1049 /S";
                    else if (appName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        args = "-s";

                    sw.WriteLine($"echo [%date% %time%] Запуск {appName} с аргументами: {args} >> \"{logFile}\"");
                    sw.WriteLine($"start \"\" /wait \"{dest}\" {args}");
                    sw.WriteLine($"echo [%date% %time%] Завершено: {appName} [Код: %errorlevel%] >> \"{logFile}\"");
                }

                sw.WriteLine($"echo. >> \"{logFile}\"");
                sw.WriteLine($"echo [%date% %time%] === ВСЕ ПРИЛОЖЕНИЯ УСТАНОВЛЕНЫ === >> \"{logFile}\"");
            }

            UpdateStatus("Развертывание завершено! Перезагрузка ПК через 10 секунд...");
            System.Threading.Thread.Sleep(10000);
            RunCommand("wpeutil.exe", "reboot");
        }

        //private void ExecuteDeployment(string imagePath, List<string> apps)
        //{
        //    UpdateStatus("Тест: создание сценария установки софта на диске E:\\...");

        //    string testScriptsDir = @"E:\Windows\Setup\Scripts";
        //    Directory.CreateDirectory(testScriptsDir);

        //    string cmdPath = Path.Combine(testScriptsDir, "SetupComplete.cmd");

        //    // Задаем правильный путь к debug.log прямо в папке E:\Windows\Setup\Scripts
        //    string logFile = Path.Combine(testScriptsDir, "debug.log");

        //    using (StreamWriter sw = File.CreateText(cmdPath))
        //    {
        //        sw.WriteLine("@echo off");
        //        sw.WriteLine("chcp 65001 > nul");

        //        // Проверка прав администратора
        //        sw.WriteLine("net session >nul 2>&1");
        //        sw.WriteLine("if %errorlevel% neq 0 (");
        //        sw.WriteLine("    echo [ОШИБКА] Запустите скрипт от имени Администратора!");
        //        sw.WriteLine("    pause");
        //        sw.WriteLine("    exit /b 1");
        //        sw.WriteLine(")");

        //        // 1. Инициализация debug.log на диске E:
        //        sw.WriteLine($"echo [%date% %time%] === ТЕСТОВЫЙ ПРОГОН (БЕЗ РЕАЛЬНОЙ УСТАНОВКИ) === > \"{logFile}\"");

        //        foreach (var appName in apps)
        //        {
        //            string source = Path.Combine(usbDrivePath, "Soft", appName);
        //            string dest = $@"C:\Windows\Temp\{appName}";

        //            sw.WriteLine($"echo. >> \"{logFile}\"");
        //            sw.WriteLine($"echo [%date% %time%] Тест копирования: \"{appName}\" >> \"{logFile}\"");

        //            // Имитация копирования (проверка наличия файла на флешке)
        //            sw.WriteLine($"if exist \"{source}\" (echo [OK] Файл найден на носителе >> \"{logFile}\") else (echo [FAIL] Исходный файл не найден! >> \"{logFile}\")");

        //            string args = "/S";
        //            if (appName.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
        //                args = "/silent /install";
        //            else if (appName.Contains("VisualC", StringComparison.OrdinalIgnoreCase) || appName.Contains("vcredist", StringComparison.OrdinalIgnoreCase))
        //                args = "/ai /gm2";
        //            else if (appName.Contains("DirectX", StringComparison.OrdinalIgnoreCase))
        //                args = "/Q /T:C:\\Windows\\Temp\\DX_Extract и запуск DXSETUP.exe /silent"; // Убрали символ '>'
        //            else if (appName.Contains("vlc", StringComparison.OrdinalIgnoreCase))
        //                args = "/L=1049 /S";
        //            else if (appName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        //                args = "-s";

        //            // Логируем имитацию вызова без опасных спецсимволов
        //            sw.WriteLine($"echo [%date% %time%] [СИМУЛЯЦИЯ ЗАПУСКА] \"{dest}\" с аргументами: {args} >> \"{logFile}\"");
        //            sw.WriteLine($"echo [%date% %time%] Программа \"{appName}\" успешно проверена >> \"{logFile}\"");
        //        }

        //        sw.WriteLine($"echo. >> \"{logFile}\"");
        //        sw.WriteLine($"echo [%date% %time%] === ТЕСТ ЗАВЕРШЕН, СОФТ НЕ ЗАТРОНУТ === >> \"{logFile}\"");
        //        sw.WriteLine($"echo Тестовый лог сформирован в {logFile}");
        //        sw.WriteLine("pause");
        //    }

        //    UpdateStatus("Тестовый скрипт без установки создан на E:\\!");
        //}






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
