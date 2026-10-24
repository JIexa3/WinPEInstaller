using System;
using System.Collections.Generic;
using System.IO;

namespace WinPEInstallerWPF
{
    public static class DeploymentScriptGenerator
    {
        public static string GetInstallerArguments(string fileName)
        {
            string name = fileName.ToLowerInvariant();

            if (name.Contains("chrome"))
                return "/silent /install";
            if (name.Contains("visualc") || name.Contains("vcredist"))
                return "/ai /gm2";
            if (name.Contains("vlc"))
                return "/L=1049 /S";
            if (name.Contains("nvidia"))
                return "-s";
            if (name.Contains("telegram"))
                return "/VERYSILENT /NORESTART /SP-";
            if (name.Contains("discord"))
                return "/s";
            if (name.Contains("acro") || name.Contains("adobe"))
                return "/sAll /rs /msi EULA_ACCEPT=YES";
            if (name.Contains("foxit") || name.Contains("furmark") || name.Contains("crystaldisk") || name.Contains("hwinfo"))
                return "/VERYSILENT";
            if (name.Contains("aida"))
                return "/SILENT";
            if (name.Contains("anydesk"))
                return "--install \"C:\\Program Files (x86)\\AnyDesk\" --silent";
            if (name.Contains("3dp"))
                return "/s";

            return "/S";
        }

        public static (string Category, string FriendlyName) CategorizeSoftware(string file)
        {
            string f = file.ToLowerInvariant();

            if (f.Contains("visualc") || f.Contains("vcredist")) return ("Системные компоненты", "Visual C++ 2005-2026");
            if (f.Contains("directx")) return ("Системные компоненты", "DirectX Legacy (D3DX)");
            if (f.Contains("7zip") || f.Contains("7z")) return ("Системные компоненты", "7-Zip Архиватор");

            if (f.Contains("chrome")) return ("Интернет и документы", "Google Chrome");
            if (f.Contains("acro") || f.Contains("adobe")) return ("Интернет и документы", "Adobe Acrobat Reader");
            if (f.Contains("torrent") || f.Contains("qbittorrent") || f.Contains("qbit")) return ("Интернет и документы", "qBittorrent");
            if (f.Contains("foxit")) return ("Интернет и документы", "Foxit PDF Reader");
            if (f.Contains("telegram")) return ("Интернет и документы", "Telegram Desktop");
            if (f.Contains("discord")) return ("Интернет и документы", "Discord");

            if (f.Contains("steam")) return ("Игры и мультимедиа", "Steam Client");
            if (f.Contains("vlc")) return ("Игры и мультимедиа", "VLC Media Player");
            if (f.Contains("nvidia")) return ("Игры и мультимедиа", "NVIDIA App");

            if (f.Contains("aida")) return ("Диагностика и стресс-тесты", "AIDA64 Extreme");
            if (f.Contains("crystaldisk")) return ("Диагностика и стресс-тесты", "CrystalDiskInfo (SSD)");
            if (f.Contains("furmark")) return ("Диагностика и стресс-тесты", "FurMark (GPU Stress)");
            if (f.Contains("hwinfo")) return ("Диагностика и стресс-тесты", "HWiNFO64");

            return ("Другие приложения", Path.GetFileNameWithoutExtension(file));
        }

        public static string BuildSetupCompleteScript(List<string> apps, bool installOffice, bool removeCopilot)
        {
            using var sw = new StringWriter();
            sw.WriteLine("@echo off");
            sw.WriteLine("chcp 65001 > nul");
            sw.WriteLine("set LOG=C:\\Windows\\Temp\\install.log");
            sw.WriteLine("echo [%date% %time%] SetupComplete started >> \"%LOG%\"");

            // Не требует флешки — выполняем первым
            if (removeCopilot)
            {
                sw.WriteLine("powershell -Command \"Get-AppxPackage -AllUsers *Copilot* | Remove-AppxPackage -AllUsers\" > nul 2>&1");
                sw.WriteLine("echo [%date% %time%] Copilot removal exit code %errorlevel% >> \"%LOG%\"");
            }

            // Автопоиск буквы флешки
            sw.WriteLine("set USB_DRIVE=");
            sw.WriteLine("for %%D in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (");
            sw.WriteLine("    if exist \"%%D:\\Soft\" set USB_DRIVE=%%D:");
            sw.WriteLine(")");
            sw.WriteLine("if not defined USB_DRIVE (");
            sw.WriteLine("    echo [%date% %time%] USB_DRIVE not found - software install skipped >> \"%LOG%\"");
            sw.WriteLine("    exit /b 1");
            sw.WriteLine(")");
            sw.WriteLine("echo [%date% %time%] USB_DRIVE=%USB_DRIVE% >> \"%LOG%\"");

            if (installOffice)
            {
                sw.WriteLine("if exist \"%USB_DRIVE%\\Soft\\Office\\AUTORUN.exe\" (");
                sw.WriteLine("    echo [%date% %time%] Installing Office LTSC >> \"%LOG%\"");
                // Запуск прямо с флешки, без копирования нескольких ГБ во временную папку
                sw.WriteLine("    start \"\" /wait \"%USB_DRIVE%\\Soft\\Office\\AUTORUN.exe\" /S /PRO /ACT /RU");
                sw.WriteLine("    echo [%date% %time%] Office exit code %errorlevel% >> \"%LOG%\"");
                sw.WriteLine(")");
            }

            foreach (var appName in apps)
            {
                if (appName.Equals("Office.exe", StringComparison.OrdinalIgnoreCase) ||
                    appName.Equals("AUTORUN.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                string dest = $@"C:\Windows\Temp\{appName}";
                sw.WriteLine($"echo [%date% %time%] Installing {appName} >> \"%LOG%\"");
                sw.WriteLine($"copy /y \"%USB_DRIVE%\\Soft\\{appName}\" \"{dest}\" > nul 2>&1");

                if (appName.Contains("DirectX", StringComparison.OrdinalIgnoreCase))
                {
                    sw.WriteLine("mkdir C:\\Windows\\Temp\\DX_Extract > nul 2>&1");
                    sw.WriteLine($"start \"\" /wait \"{dest}\" /Q /T:C:\\Windows\\Temp\\DX_Extract");
                    sw.WriteLine("start \"\" /wait \"C:\\Windows\\Temp\\DX_Extract\\DXSETUP.exe\" /silent");
                    sw.WriteLine("echo [%date% %time%] DirectX exit code %errorlevel% >> \"%LOG%\"");
                    sw.WriteLine("rmdir /s /q C:\\Windows\\Temp\\DX_Extract > nul 2>&1");
                    sw.WriteLine($"del /q \"{dest}\" > nul 2>&1");
                    continue;
                }

                string args = GetInstallerArguments(appName);
                sw.WriteLine($"start \"\" /wait \"{dest}\" {args}");
                sw.WriteLine($"echo [%date% %time%] {appName} exit code %errorlevel% >> \"%LOG%\"");
                sw.WriteLine($"del /q \"{dest}\" > nul 2>&1");
            }

            sw.WriteLine("echo [%date% %time%] SetupComplete finished >> \"%LOG%\"");
            return sw.ToString();
        }
    }
}