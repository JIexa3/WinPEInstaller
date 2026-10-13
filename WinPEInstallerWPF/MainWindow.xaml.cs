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
            // 1. Проверяем накопитель, с которого запущена сама программа
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string appDrive = Path.GetPathRoot(appDir);

            if (!string.IsNullOrEmpty(appDrive) &&
                Directory.Exists(Path.Combine(appDrive, "Images")) &&
                Directory.Exists(Path.Combine(appDrive, "Soft")))
            {
                usbDrivePath = appDrive;
                LblStatus.Text = $"Носитель определен: {usbDrivePath}";
                return;
            }

            // 2. Если запущено из RAM-диска (X:\) в WinPE — ищем флешку среди всех дисков
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    // Проверяем только готовые к чтению диски и пропускаем временный диск X:\
                    if (drive.IsReady && !drive.Name.StartsWith("X", StringComparison.OrdinalIgnoreCase))
                    {
                        string imagesFolder = Path.Combine(drive.Name, "Images");
                        string softFolder = Path.Combine(drive.Name, "Soft");

                        // Ищем накопитель, содержащий обе наши рабочие папки
                        if (Directory.Exists(imagesFolder) && Directory.Exists(softFolder))
                        {
                            usbDrivePath = drive.Name;
                            LblStatus.Text = $"Флешка обнаружена: {usbDrivePath}";
                            return;
                        }
                    }
                }
                catch
                {
                    // Игнорируем дисководы или заблокированные картридеры
                }
            }

            // 3. Если папки не найдены ни на одном диске
            usbDrivePath = "";
            LblStatus.Text = "Ошибка: Флешка с папками 'Images' и 'Soft' не найдена!";
            MessageBox.Show(
                "Не удалось автоматически найти установочную флешку.\n" +
                "Убедитесь, что в корне накопителя созданы папки 'Images' и 'Soft'.",
                "Носитель не найден",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

            var selectedEdition = (WindowsEditionItem)ComboImages.SelectedItem;

            var selectedApps = new List<string>();
            if (ListSoft.ItemsSource != null)
            {
                foreach (SoftwareItem item in ListSoft.ItemsSource)
                {
                    if (item.IsSelected) selectedApps.Add(item.Name);
                }
            }

            // Считываем состояние чекбоксов твиков
            bool disableDefender = ChkDisableDefender.IsChecked == true;
            bool removeCopilot = ChkRemoveCopilot.IsChecked == true;
            bool disableTelemetry = ChkDisableTelemetry.IsChecked == true;
            bool disableEdge = ChkDisableEdgeStartup.IsChecked == true;

            BtnStart.IsEnabled = false;
            ProgressInstall.IsIndeterminate = true;

            await Task.Run(() => ExecuteDeployment(
                selectedEdition.FilePath,
                selectedEdition.Index,
                selectedApps,
                disableDefender,
                removeCopilot,
                disableTelemetry,
                disableEdge
            ));
        }






        private void ExecuteDeployment(
        string imagePath,
        int imageIndex,
        List<string> apps,
        bool disableDefender,
        bool removeCopilot,
        bool disableTelemetry,
        bool disableEdge)
        {
            // 1. Форматирование диска 0
            UpdateStatus("Конфигурация целевого накопителя (Diskpart)...");
            string diskpartScript = Path.Combine(Path.GetTempPath(), "dp.txt");
            File.WriteAllText(diskpartScript, "select disk 0\nclean\nconvert gpt\ncreate partition efi size=100\nformat quick fs=fat32 label=\"System\"\nassign letter=S\ncreate partition primary\nformat quick fs=ntfs label=\"Windows\"\nassign letter=W\nexit");
            RunCommand("diskpart.exe", $"/s \"{diskpartScript}\"");

            // 2. Развертывание ОС через DISM
            UpdateStatus($"Развертывание редакции (Индекс {imageIndex})... Это займет 5-8 минут.");
            RunCommand("dism.exe", $"/Apply-Image /ImageFile:\"{imagePath}\" /Index:{imageIndex} /ApplyDir:W:\\");

            // 3. Создание системного загрузчика UEFI
            UpdateStatus("Создание системного загрузчика BCD...");
            RunCommand("W:\\Windows\\System32\\bcdboot.exe", "W:\\Windows /s S: /f UEFI");

            // 4. Применение твиков реестра в неактивную систему
            UpdateStatus("Применение твиков реестра и оптимизаций...");
            ApplyOfflineRegistryTweaks(disableDefender, removeCopilot, disableTelemetry, disableEdge);

            // 5. Запись SetupComplete.cmd на системный диск W:\
            UpdateStatus("Запись конфигурации SetupComplete.cmd...");
            string cmdPath = @"W:\Windows\Setup\Scripts\SetupComplete.cmd";
            Directory.CreateDirectory(Path.GetDirectoryName(cmdPath));

            using (StreamWriter sw = File.CreateText(cmdPath))
            {
                sw.WriteLine("@echo off");
                sw.WriteLine("chcp 65001 > nul");

                // БАТНИК САМ ИЩЕТ БУКВУ ФЛЕШКИ ПРИ ПЕРВОМ ВХОДЕ В WINDOWS
                sw.WriteLine(":: Автопоиск буквы флешки по наличию папки Soft");
                sw.WriteLine("set USB_DRIVE=");
                sw.WriteLine("for %%D in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (");
                sw.WriteLine("    if exist \"%%D:\\Soft\" set USB_DRIVE=%%D:");
                sw.WriteLine(")");
                sw.WriteLine("if not defined USB_DRIVE (");
                sw.WriteLine("    echo [ОШИБКА] Флешка с софтом не найдена! >> C:\\Windows\\Temp\\install.log");
                sw.WriteLine("    exit /b 1");
                sw.WriteLine(")");

                if (removeCopilot)
                {
                    sw.WriteLine("powershell -Command \"Get-AppxPackage -AllUsers *Copilot* | Remove-AppxPackage -AllUsers\" > nul 2>&1");
                    sw.WriteLine("powershell -Command \"Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*Copilot*' | Remove-AppxProvisionedPackage -Online\" > nul 2>&1");
                }

                foreach (var appName in apps)
                {
                    string dest = $@"C:\Windows\Temp\{appName}";

                    // Копируем софт, используя найденную переменную %USB_DRIVE%
                    sw.WriteLine($"copy /y \"%USB_DRIVE%\\Soft\\{appName}\" \"{dest}\" > nul 2>&1");

                    string args = "/S";
                    if (appName.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                        args = "/silent /install";
                    else if (appName.Contains("VisualC", StringComparison.OrdinalIgnoreCase) || appName.Contains("vcredist", StringComparison.OrdinalIgnoreCase))
                        args = "/ai /gm2";
                    else if (appName.Contains("DirectX", StringComparison.OrdinalIgnoreCase))
                    {
                        sw.WriteLine("mkdir C:\\Windows\\Temp\\DX_Extract > nul 2>&1");
                        sw.WriteLine($"start \"\" /wait \"{dest}\" /Q /T:C:\\Windows\\Temp\\DX_Extract");
                        sw.WriteLine("start \"\" /wait \"C:\\Windows\\Temp\\DX_Extract\\DXSETUP.exe\" /silent");
                        sw.WriteLine("rmdir /s /q C:\\Windows\\Temp\\DX_Extract > nul 2>&1");
                        continue;
                    }
                    else if (appName.Contains("vlc", StringComparison.OrdinalIgnoreCase))
                        args = "/L=1049 /S";
                    else if (appName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        args = "-s";

                    sw.WriteLine($"start \"\" /wait \"{dest}\" {args}");
                }
            }


            UpdateStatus("Развертывание завершено! Перезагрузка ПК через 10 секунд...");
            System.Threading.Thread.Sleep(10000);
            RunCommand("wpeutil.exe", "reboot");
        }


        // Метод монтирования и правки реестра развернутой Windows W:\
        private void ApplyOfflineRegistryTweaks(bool disableDefender, bool removeCopilot, bool disableTelemetry, bool disableEdge)
        {
            try
            {
                // Подключаем куст реестра HKLM\SOFTWARE распакованной системы
                RunCommand("reg.exe", @"load HKLM\OFFLINE_SOFT W:\Windows\System32\config\SOFTWARE");

                if (disableDefender)
                {
                    RunCommand("reg.exe", @"add ""HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows Defender"" /v DisableAntiSpyware /t REG_DWORD /d 1 /f");
                    RunCommand("reg.exe", @"add ""HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableRealtimeMonitoring /t REG_DWORD /d 1 /f");
                    RunCommand("reg.exe", @"add ""HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableBehaviorMonitoring /t REG_DWORD /d 1 /f");
                }

                if (removeCopilot)
                {
                    RunCommand("reg.exe", @"add HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows\WindowsCopilot /v TurnOffWindowsCopilot /t REG_DWORD /d 1 /f");
                    RunCommand("reg.exe", @"add HKLM\OFFLINE_SOFT\Microsoft\Windows\CurrentVersion\Explorer\Advanced /v ShowCopilotButton /t REG_DWORD /d 0 /f");
                }

                if (disableTelemetry)
                {
                    RunCommand("reg.exe", @"add HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows\DataCollection /v AllowTelemetry /t REG_DWORD /d 0 /f");
                    RunCommand("reg.exe", @"add HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows\DataCollection /v MaxTelemetryAllowed /t REG_DWORD /d 0 /f");
                }

                if (disableEdge)
                {
                    RunCommand("reg.exe", @"add HKLM\OFFLINE_SOFT\Policies\Microsoft\Edge /v StartupBoostEnabled /t REG_DWORD /d 0 /f");
                    RunCommand("reg.exe", @"add HKLM\OFFLINE_SOFT\Policies\Microsoft\Edge /v BackgroundModeEnabled /t REG_DWORD /d 0 /f");
                }

                // Обязательно выгружаем и сохраняем куст реестра
                RunCommand("reg.exe", @"unload HKLM\OFFLINE_SOFT");
            }
            catch
            {
                // Игнорируем или логируем ошибки доступа к реестру
            }
        }


        

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
