using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WinPEInstallerWPF
{
    public partial class MainWindow : Window
    {
        private string usbDrivePath = "";
        private string logPath = "";
        private readonly object logLock = new();
        private int? bootDiskNumber;          // номер диска, на котором лежит сама флешка
        private bool isDarkTheme = true;

        // Консольные утилиты (diskpart, dism) пишут в OEM-кодировке (cp866 на русской системе)
        private static readonly Encoding OemEncoding = GetOemEncoding();

        private static Encoding GetOemEncoding()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch { return Encoding.Default; }
        }

        // Ключ MiniNT существует только в WinPE
        private static bool IsWinPE
        {
            get
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");
                    return key != null;
                }
                catch { return false; }
            }
        }

        // ───────────────────────── Модели ─────────────────────────

        public class SoftwareItem
        {
            public string Name { get; set; } = "";          // Имя реального файла
            public string DisplayName { get; set; } = "";   // Красивое имя на экране
            public string Category { get; set; } = "";
            public bool IsSelected { get; set; } = true;
            public System.Windows.Media.ImageSource? Icon { get; set; }
        }

        public class WindowsEditionItem
        {
            public string DisplayName { get; set; } = "";
            public string FilePath { get; set; } = "";
            public int Index { get; set; }
            public override string ToString() => DisplayName;
        }

        public class DiskItem
        {
            public int Number { get; set; }
            public string Size { get; set; } = "";
            public string Model { get; set; } = "";
            public string BusType { get; set; } = "";
            public bool IsBootMedia { get; set; }
            public string Status { get; set; } = "";
            public bool IsRunningSystem { get; set; }

            public override string ToString()
            {
                string s = $"Диск {Number} — {Size}";
                if (!string.IsNullOrWhiteSpace(Model)) s += $" — {Model}";
                if (!string.IsNullOrWhiteSpace(BusType)) s += $" [{BusType}]";
                if (!string.IsNullOrWhiteSpace(Status) &&
                    !Status.Equals("Online", StringComparison.OrdinalIgnoreCase) &&
                    !Status.Equals("В сети", StringComparison.OrdinalIgnoreCase))
                    s += $" ({Status})";
                if (IsBootMedia) s += "  ⛔ ЗАГРУЗОЧНАЯ ФЛЕШКА";
                if (IsRunningSystem) s += "  ⚠ ТЕКУЩАЯ WINDOWS";
                return s;
            }
        }

        // ───────────────────────── Инициализация ─────────────────────────

        public MainWindow()
        {
            InitializeComponent();
            InitLog();
            FindUsbDrive();
            LoadWindowsImages();
            LoadAdditionalSoftware();
            Loaded += async (_, _) => await LoadDisksAsync();
        }

        private void InitLog()
        {
            logPath = Directory.Exists(@"X:\")
                ? @"X:\deploy.log"
                : Path.Combine(Path.GetTempPath(), "deploy.log");
            Log($"=== Запуск инсталлера (WinPE: {IsWinPE}, OEM codepage: {OemEncoding.CodePage}) ===");
        }

        private void Log(string message)
        {
            try
            {
                lock (logLock)
                    File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { /* лог не должен ронять установку */ }
        }

        private void FlushLogToUsb()
        {
            try
            {
                if (string.IsNullOrEmpty(usbDrivePath) || !File.Exists(logPath)) return;
                string dir = Path.Combine(usbDrivePath, "logs");
                Directory.CreateDirectory(dir);
                File.Copy(logPath, Path.Combine(dir, $"deploy_{DateTime.Now:yyyyMMdd_HHmmss}.log"), true);
            }
            catch { }
        }

        private System.Windows.Media.ImageSource? GetIconFromFile(string filePath)
        {
            try
            {
                using (System.Drawing.Icon? sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath))
                {
                    if (sysIcon != null)
                    {
                        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch { }
            return null;
        }

        private void FindUsbDrive()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string appDrive = Path.GetPathRoot(appDir) ?? "";

            if (!string.IsNullOrEmpty(appDrive) &&
                Directory.Exists(Path.Combine(appDrive, "Images")) &&
                Directory.Exists(Path.Combine(appDrive, "Soft")))
            {
                usbDrivePath = appDrive;
                LblStatus.Text = $"Носитель определен: {usbDrivePath}";
                Log($"USB (по пути приложения): {usbDrivePath}");
                return;
            }

            var candidates = new List<DriveInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && !drive.Name.StartsWith("X", StringComparison.OrdinalIgnoreCase) &&
                        Directory.Exists(Path.Combine(drive.Name, "Images")) &&
                        Directory.Exists(Path.Combine(drive.Name, "Soft")))
                    {
                        candidates.Add(drive);
                    }
                }
                catch { }
            }

            // Сменные носители (USB-флешки) в приоритете: на постоянных дисках папки Images/Soft могут просто лежать «на хранении»
            var chosen = candidates.OrderBy(d => d.DriveType == DriveType.Removable ? 0 : 1).FirstOrDefault();
            if (chosen != null)
            {
                usbDrivePath = chosen.Name;
                LblStatus.Text = $"Флешка обнаружена: {usbDrivePath}";
                Log($"USB (поиск по дискам): {usbDrivePath}; кандидаты: " +
                    string.Join(", ", candidates.Select(d => $"{d.Name} [{d.DriveType}]")));
                return;
            }

            usbDrivePath = "";
            LblStatus.Text = "Ошибка: Флешка с папками 'Images' и 'Soft' не найдена!";
            Log("USB не найдена");
            MessageBox.Show(
                "Не удалось автоматически найти установочную флешку.\n" +
                "Убедитесь, что в корне накопителя созданы папки 'Images' и 'Soft'.",
                "Носитель не найден", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ───────────────────────── Образы Windows ─────────────────────────

        private void LoadWindowsImages()
        {
            string imagesPath = Path.Combine(usbDrivePath, "Images");
            if (!Directory.Exists(imagesPath)) return;

            ComboImages.Items.Clear();

            string win11Path = Path.Combine(imagesPath, "Win11.wim");
            if (File.Exists(win11Path))
            {
                ComboImages.Items.Add(new WindowsEditionItem { DisplayName = "Windows 11 Профессиональная (Pro)", FilePath = win11Path, Index = 4 });
                ComboImages.Items.Add(new WindowsEditionItem { DisplayName = "Windows 11 Домашняя (Home)", FilePath = win11Path, Index = 1 });
                ComboImages.Items.Add(new WindowsEditionItem { DisplayName = "Windows 11 Домашняя для одного языка", FilePath = win11Path, Index = 2 });
                ComboImages.Items.Add(new WindowsEditionItem { DisplayName = "Windows 11 Для образовательных учреждений", FilePath = win11Path, Index = 3 });
            }

            string win10Path = Path.Combine(imagesPath, "Win10.wim");
            if (File.Exists(win10Path))
            {
                ComboImages.Items.Add(new WindowsEditionItem { DisplayName = "Windows 10 Профессиональная (Pro)", FilePath = win10Path, Index = 2 });
                ComboImages.Items.Add(new WindowsEditionItem { DisplayName = "Windows 10 Домашняя (Home)", FilePath = win10Path, Index = 1 });
            }

            if (ComboImages.Items.Count == 0)
            {
                foreach (var file in Directory.GetFiles(imagesPath, "*.wim"))
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

        // ───────────────────────── Софт (логика в DeploymentScriptGenerator) ─────────────────────────

        private void LoadAdditionalSoftware()
        {
            string softPath = Path.Combine(usbDrivePath, "Soft");
            var softwareList = new List<SoftwareItem>();

            if (Directory.Exists(softPath))
            {
                foreach (var app in Directory.GetFiles(softPath, "*.exe"))
                {
                    string fileName = Path.GetFileName(app);

                    // Office ставится отдельной галочкой
                    if (fileName.Equals("Office.exe", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("AUTORUN.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var (category, friendlyName) = DeploymentScriptGenerator.CategorizeSoftware(fileName);

                    softwareList.Add(new SoftwareItem
                    {
                        Name = fileName,
                        DisplayName = friendlyName,
                        Category = category,
                        IsSelected = true,
                        Icon = GetIconFromFile(app)
                    });
                }
            }

            var view = (System.Windows.Data.ListCollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(softwareList);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Category"));
            ListSoft.ItemsSource = view;
        }

        // ───────────────────────── Диски ─────────────────────────

        private string RunDiskpart(string script, bool throwOnError = true)
        {
            string path = Path.Combine(Path.GetTempPath(), $"dp_{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, script, Encoding.ASCII);
            try { return RunCommandCapture("diskpart.exe", $"/s \"{path}\"", throwOnError); }
            finally { try { File.Delete(path); } catch { } }
        }

        /// <summary>Номер физического диска, на котором лежит флешка (через Win32 API, без diskpart).</summary>
        private int? GetBootDiskNumber()
        {
            string root = usbDrivePath.TrimEnd('\\');
            if (root.Length < 2 || root[1] != ':') return null;
            try { return PhysicalDisks.GetDiskNumberOfDrive(root[0]); }
            catch (Exception ex) { Log($"GetBootDiskNumber: {ex.Message}"); return null; }
        }

        private static string FormatSize(long bytes)
        {
            double gb = bytes / 1073741824.0;
            return gb >= 1024
                ? $"{(gb / 1024).ToString("0.#", CultureInfo.InvariantCulture)} TB"
                : $"{gb.ToString("0", CultureInfo.InvariantCulture)} GB";
        }

        private List<DiskItem> DetectDisks()
        {
            var result = new List<DiskItem>();
            bootDiskNumber = GetBootDiskNumber();

            int? systemDisk = null;
            try
            {
                string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
                if (sysRoot.Length >= 2 && sysRoot[1] == ':')
                    systemDisk = PhysicalDisks.GetDiskNumberOfDrive(sysRoot[0]);
            }
            catch { }

            Log($"Диск флешки: {(bootDiskNumber?.ToString() ?? "не определён")}; диск текущей системы: {(systemDisk?.ToString() ?? "нет")}");

            foreach (var d in PhysicalDisks.Enumerate())
            {
                Log($"Найден диск {d.Number}: {FormatSize(d.SizeBytes)}, '{d.Model}', шина '{d.BusType}'");
                result.Add(new DiskItem
                {
                    Number = d.Number,
                    Size = FormatSize(d.SizeBytes),
                    Model = d.Model,
                    BusType = d.BusType,
                    IsBootMedia = bootDiskNumber == d.Number,
                    IsRunningSystem = systemDisk == d.Number
                });
            }

            if (result.Count == 0)
                Log("Физические диски не найдены.");
            return result;
        }

        private async Task LoadDisksAsync()
        {
            BtnStart.IsEnabled = false;
            BtnRefreshDisks.IsEnabled = false;
            ComboDisks.IsEnabled = false;
            LblStatus.Text = "Поиск дисков...";
            try
            {
                var disks = await Task.Run(DetectDisks);
                ComboDisks.Items.Clear();
                foreach (var d in disks) ComboDisks.Items.Add(d);

                // Предвыбор: внутренний (не USB) диск, если он единственный кандидат.
                var candidates = disks.Where(d => !d.IsBootMedia &&
                                                  !d.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase)).ToList();
                ComboDisks.SelectedItem = candidates.Count == 1 ? candidates[0] : null;

                LblStatus.Text = disks.Count == 0
                    ? "Диски не найдены (см. лог: " + logPath + ")"
                    : "Выберите образ и диск для установки";
            }
            catch (Exception ex)
            {
                Log($"LoadDisksAsync: {ex}");
                LblStatus.Text = "Ошибка поиска дисков: " + ex.Message;
            }
            finally
            {
                BtnStart.IsEnabled = !string.IsNullOrEmpty(usbDrivePath);
                BtnRefreshDisks.IsEnabled = true;
                ComboDisks.IsEnabled = true;
            }
        }

        private async void BtnRefreshDisks_Click(object sender, RoutedEventArgs e) => await LoadDisksAsync();

        private void ComboDisks_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ComboDisks.SelectedItem is DiskItem d && d.IsBootMedia)
            {
                MessageBox.Show("Это диск, с которого запущен установщик (ваша флешка). Выберите другой диск.",
                    "Недопустимый выбор", MessageBoxButton.OK, MessageBoxImage.Stop);
                ComboDisks.SelectedItem = null;
            }
        }

        // ───────────────────────── Запуск установки ─────────────────────────

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Защита: разметка и развёртывание разрешены только внутри WinPE
            if (!IsWinPE)
            {
                MessageBox.Show(
                    "Установка отключена: программа запущена не в WinPE.\n" +
                    "Интерфейс и список дисков можно проверять, но разметка и развёртывание заблокированы,\n" +
                    "чтобы не стереть диски рабочего компьютера.\n\n" +
                    "Для проверки установки используйте виртуальную машину.",
                    "Защита", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (string.IsNullOrEmpty(usbDrivePath))
            {
                MessageBox.Show("Установочная флешка не найдена.");
                return;
            }
            if (ComboImages.SelectedItem is not WindowsEditionItem edition)
            {
                MessageBox.Show("Укажите требуемый образ Windows!");
                return;
            }
            if (ComboDisks.SelectedItem is not DiskItem disk)
            {
                MessageBox.Show("Выберите диск, на который будет установлена система!");
                return;
            }
            if (disk.IsBootMedia)
            {
                MessageBox.Show("Нельзя устанавливать систему на загрузочную флешку.");
                return;
            }

            string warn = "";
            if (!bootDiskNumber.HasValue)
                warn += "\n\n⚠ Не удалось определить, на каком диске находится флешка. Проверьте выбор вручную!";
            if (disk.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase))
                warn += "\n\n⚠ Выбран USB-накопитель.";

            var confirm = MessageBox.Show(
                $"ВСЕ ДАННЫЕ на диске будут безвозвратно удалены:\n\n{disk}{warn}\n\nПродолжить?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            var selectedApps = new List<string>();
            if (ListSoft.ItemsSource != null)
            {
                foreach (SoftwareItem item in ListSoft.ItemsSource)
                    if (item.IsSelected) selectedApps.Add(item.Name);
            }

            bool installOffice = ChkInstallOffice.IsChecked == true;
            bool disableDefender = ChkDisableDefender.IsChecked == true;
            bool removeCopilot = ChkRemoveCopilot.IsChecked == true;
            bool disableTelemetry = ChkDisableTelemetry.IsChecked == true;
            bool disableEdge = ChkDisableEdgeStartup.IsChecked == true;

            SetUiBusy(true);
            try
            {
                await Task.Run(() => ExecuteDeployment(
                    edition.FilePath, edition.Index, disk.Number, selectedApps,
                    installOffice, disableDefender, removeCopilot, disableTelemetry, disableEdge));
            }
            catch (Exception ex)
            {
                Log($"ОШИБКА: {ex}");
                FlushLogToUsb();
                LblStatus.Text = "Ошибка: " + ex.Message;
                MessageBox.Show($"{ex.Message}\n\nЛог: {logPath}\n(копия — на флешке в папке logs, если она доступна на запись)",
                    "Сбой установки", MessageBoxButton.OK, MessageBoxImage.Error);
                SetUiBusy(false);
            }
        }

        private void SetUiBusy(bool busy)
        {
            BtnStart.IsEnabled = !busy;
            BtnRefreshDisks.IsEnabled = !busy;
            ComboDisks.IsEnabled = !busy;
            ComboImages.IsEnabled = !busy;
            ProgressInstall.IsIndeterminate = busy;
            if (!busy) ProgressInstall.Value = 0;
        }

        // Подбор двух свободных букв для EFI и Windows (S/W могут быть заняты флешкой)
        private static (string efi, string win) PickFreeLetters()
        {
            var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
            var free = "SWTUVYZRQP".Where(c => !used.Contains(c)).ToList();
            if (free.Count < 2) throw new InvalidOperationException("Нет свободных букв дисков для разделов.");
            return (free[0].ToString(), free[1].ToString());
        }

        private void ExecuteDeployment(
            string imagePath, int imageIndex, int targetDisk, List<string> apps,
            bool installOffice, bool disableDefender, bool removeCopilot,
            bool disableTelemetry, bool disableEdge)
        {
            Log($"Старт: образ={imagePath}, индекс={imageIndex}, диск={targetDisk}");

            // Повторная проверка перед стиранием — на случай, если нумерация дисков изменилась
            int? bootNow = GetBootDiskNumber();
            if (bootNow == targetDisk)
                throw new InvalidOperationException($"Диск {targetDisk} — это загрузочная флешка. Установка остановлена.");

            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Файл образа не найден", imagePath);

            var (efi, win) = PickFreeLetters();
            Log($"Буквы: EFI={efi}:, Windows={win}:");

            // 1. Разметка диска
            UpdateStatus($"Разметка диска {targetDisk} (diskpart)...", null);
            RunDiskpart(
                $"select disk {targetDisk}\n" +
                "online disk noerr\n" +
                "attributes disk clear readonly noerr\n" +
                "clean\n" +
                "convert gpt\n" +
                "create partition efi size=260\n" +
                "format quick fs=fat32 label=System\n" +
                $"assign letter={efi}\n" +
                "create partition msr size=16\n" +
                "create partition primary\n" +
                "format quick fs=ntfs label=Windows\n" +
                $"assign letter={win}\n" +
                "exit");

            if (!Directory.Exists($@"{efi}:\") || !Directory.Exists($@"{win}:\"))
                throw new InvalidOperationException("После разметки не появились разделы EFI/Windows.");

            // 2. Развёртывание образа с прогрессом
            UpdateStatus($"Развёртывание редакции (индекс {imageIndex})...", 0);
            RunDismWithProgress($"/Apply-Image /ImageFile:\"{imagePath}\" /Index:{imageIndex} /ApplyDir:{win}:\\");

            if (!File.Exists($@"{win}:\Windows\System32\config\SOFTWARE"))
                throw new InvalidOperationException("DISM завершился, но система не развёрнута (нет куста SOFTWARE).");

            // 3. Загрузчик UEFI
            UpdateStatus("Создание загрузчика (bcdboot)...", null);
            RunCommandCapture($@"{win}:\Windows\System32\bcdboot.exe", $@"{win}:\Windows /s {efi}: /f UEFI");
            if (!File.Exists($@"{efi}:\EFI\Microsoft\Boot\bootmgfw.efi"))
                throw new InvalidOperationException("bcdboot не создал EFI-загрузчик.");

            // 4. Офлайн-твики реестра (сбой не фатален — система уже установлена)
            UpdateStatus("Применение твиков реестра...", null);
            bool tweaksOk = ApplyOfflineRegistryTweaks(win, disableDefender, removeCopilot, disableTelemetry, disableEdge);

            // 5. SetupComplete.cmd (логика — в DeploymentScriptGenerator, её покрывают unit-тесты)
            UpdateStatus("Запись SetupComplete.cmd...", null);
            string cmdPath = $@"{win}:\Windows\Setup\Scripts\SetupComplete.cmd";
            Directory.CreateDirectory(Path.GetDirectoryName(cmdPath)!);
            File.WriteAllText(cmdPath,
                DeploymentScriptGenerator.BuildSetupCompleteScript(apps, installOffice, removeCopilot),
                new UTF8Encoding(false));
            Log($"SetupComplete.cmd записан ({apps.Count} приложений)");

            FlushLogToUsb();

            UpdateStatus(tweaksOk
                ? "Готово! Перезагрузка через 10 секунд..."
                : "Готово, но твики реестра применились не полностью (см. лог). Перезагрузка через 10 секунд...", 100);
            System.Threading.Thread.Sleep(10000);
            RunCommandCapture("wpeutil.exe", "reboot", false);
        }

        private bool ApplyOfflineRegistryTweaks(string win, bool disableDefender, bool removeCopilot,
                                                bool disableTelemetry, bool disableEdge)
        {
            bool loaded = false;
            try
            {
                RunCommandCapture("reg.exe", $@"load HKLM\OFFLINE_SOFT {win}:\Windows\System32\config\SOFTWARE");
                loaded = true;

                if (disableDefender)
                {
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", 1);
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring", 1);
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableBehaviorMonitoring", 1);
                }
                if (removeCopilot)
                {
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
                    Reg(@"HKLM\OFFLINE_SOFT\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0);
                }
                if (disableTelemetry)
                {
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Windows\DataCollection", "MaxTelemetryAllowed", 0);
                }
                if (disableEdge)
                {
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
                    Reg(@"HKLM\OFFLINE_SOFT\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"WARNING: твики реестра: {ex.Message}");
                return false;
            }
            finally
            {
                if (loaded)
                {
                    try
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        RunCommandCapture("reg.exe", @"unload HKLM\OFFLINE_SOFT");
                    }
                    catch (Exception ex) { Log($"WARNING: reg unload: {ex.Message}"); }
                }
            }
        }

        private void Reg(string key, string name, int value) =>
            RunCommandCapture("reg.exe", $"add \"{key}\" /v {name} /t REG_DWORD /d {value} /f");

        // ───────────────────────── Запуск процессов ─────────────────────────

        private static string Tail(string s) => s.Length <= 300 ? s.Trim() : "…" + s[^300..].Trim();

        private string RunCommandCapture(string file, string args, bool throwOnError = true)
        {
            Log($">>> {file} {args}");
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {file}");
            var errTask = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            string err = errTask.Result;

            if (!string.IsNullOrWhiteSpace(output)) Log(output.Trim());
            if (!string.IsNullOrWhiteSpace(err)) Log("[stderr] " + err.Trim());
            Log($"[exit {p.ExitCode}]");

            if (throwOnError && p.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(file)} завершился с кодом {p.ExitCode}. {Tail(output + err)}");
            return output;
        }

        // DISM пишет прогресс как "[====   10.0%   ]" с символом \r — читаем посимвольно
        private void RunDismWithProgress(string args)
        {
            Log($">>> dism.exe {args}");
            var psi = new ProcessStartInfo("dism.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = OemEncoding,
                StandardErrorEncoding = OemEncoding
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить dism.exe");
            var errTask = p.StandardError.ReadToEndAsync();
            var sb = new StringBuilder();
            var line = new StringBuilder();
            int ch;

            while ((ch = p.StandardOutput.Read()) != -1)
            {
                char c = (char)ch;
                if (c != '\r' && c != '\n') { line.Append(c); continue; }
                if (line.Length == 0) continue;

                string s = line.ToString();
                line.Clear();

                var m = Regex.Match(s, @"(\d{1,3}(?:[.,]\d)?)%");
                if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                {
                    UpdateStatus($"Развёртывание образа... {pct:0}%", pct);
                }
                if (!s.TrimStart().StartsWith("[")) sb.AppendLine(s); // строки прогресса в лог не пишем
            }
            p.WaitForExit();
            string err = errTask.Result;

            Log(sb.ToString().Trim());
            if (!string.IsNullOrWhiteSpace(err)) Log("[stderr] " + err.Trim());
            Log($"[exit {p.ExitCode}]");

            if (p.ExitCode != 0)
                throw new InvalidOperationException($"DISM завершился с кодом {p.ExitCode}. {Tail(sb + err)}");
        }

        /// <param name="progress">null — неопределённый прогресс, число 0..100 — процент</param>
        private void UpdateStatus(string message, double? progress)
        {
            Dispatcher.Invoke(() =>
            {
                LblStatus.Text = message;
                if (progress.HasValue)
                {
                    ProgressInstall.IsIndeterminate = false;
                    ProgressInstall.Value = progress.Value;
                }
                else
                {
                    ProgressInstall.IsIndeterminate = true;
                }
            });
        }

        // ───────────────────────── Тема ─────────────────────────

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Resources.MergedDictionaries.Clear();
            string themeFile = isDarkTheme ? "LightTheme.xaml" : "DarkTheme.xaml";

            try
            {
                var newTheme = new ResourceDictionary { Source = new Uri(themeFile, UriKind.RelativeOrAbsolute) };
                Application.Current.Resources.MergedDictionaries.Add(newTheme);
                isDarkTheme = !isDarkTheme;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить файл темы '{themeFile}'.\nОшибка: {ex.Message}", "Ошибка тем оформления");
            }
        }
    }
}