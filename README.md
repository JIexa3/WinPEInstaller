# 🚀 Windows Deployment Suite (WinPE WPF Installer)

Автоматизированный программный комплекс для развертывания операционных систем **Windows 11** и **Windows 10** на пустые накопители (Bare-metal) в среде **Windows PE** с последующей автоматической оптимизацией и тихой установкой пакета прикладного ПО.

---

## 📌 Ключевые возможности

* **Полная автономность (Offline-ready):** Программа собирается в виде единого исполняемого файла (`Single-File Self-Contained`), не требующего предустановленного .NET Runtime на целевом компьютере.
* **Автоматическое определение носителя:** Программа динамически находит рабочую флешку по сигнатурам каталогов `Images` и `Soft`, полностью исключая сбои из-за плавающих букв дисков в WinPE (`D:`, `E:`, `X:`).
* **Поддержка многоиндексных WIM-образов:** Чтение и развертывание конкретных редакций ОС (Windows 11 Pro / Home / Enterprise) с передачей точного номера индекса в утилиту `DISM`.
* **Оффлайн-твики и оптимизация:** Внесение параметров в системный куст реестра `SOFTWARE` до первого старта Windows:
  * Отключение Защитника Windows (Windows Defender).
  * Полное вырезание и удаление компонентов Windows Copilot (AppX).
  * Отключение системной телеметрии и сбора диагностических данных.
  * Блокировка автозапуска и фоновой активности Microsoft Edge.
* **Структурированный пакет ПО:** Интерактивная панель выбора софта с группировкой по категориям и автоматическим извлечением оригинальных иконок из исполняемых файлов.
* **Надежность:** Ключевая логика генерации скриптов и парсинга покрыта автоматическими модульными тестами на базе **xUnit**.

---

## 🗂 Структура каталогов на загрузочной флешке

Для корректной работы инсталлятора накопитель (32 ГБ / 64 ГБ) размечается в схему **GPT / UEFI (NTFS)** со следующей структурой файлов в корне:

```text
E:\ (Корень флешки)
├── boot\                          # Системные файлы загрузки WinPE
├── efi\                           # Загрузочные файлы UEFI (BOOTX64.EFI)
├── sources\
│   └── boot.wim                   # Образ среды Windows PE
├── bootmgr
├── bootmgr.efi
│
├── WinPEInstallerWPF.exe          # Скомпилированный установщик (Single-File)
│
├── Images\                        # Каталог с установочными WIM-образами
│   ├── Win11.wim                  # Оригинальный образ Windows 11 (24H2)
│   └── Win10.wim                  # Оригинальный образ Windows 10 (опционально)
│
└── Soft\                          # Каталог с автономными установщиками ПО
    ├── 7zip.exe                   # Архиватор 7-Zip
    ├── ChromeBrowser.exe          # Автономный пакет Google Chrome (Standalone)
    ├── DirectX.exe                # DirectX Redistributable (Jun2010)
    ├── VisualC++.exe              # Пакет Visual C++ Redistributable AIO
    ├── vlc-3.0.24-win64.exe       # VLC Media Player
    ├── SteamSetup.exe             # Клиент Steam
    ├── NVIDIA_app.exe             # Драйверы и панель NVIDIA App
    ├── qbittorrent.exe            # Торрент-клиент qBittorrent
    ├── AcroRdrDC.exe              # Adobe Acrobat Reader DC
    ├── AIDA64.exe                 # Диагностика AIDA64 Extreme
    ├── CrystalDiskInfo.exe        # Диагностика накопителей SSD/HDD
    ├── FurMark.exe                # Стресс-тест видеокарты FurMark
    └── Office\                    # Автономный пакет Microsoft Office
        └── AUTORUN.exe            # Репак Microsoft Office LTSC
```

---

## ⚙️ Этапы автоматического развертывания

1. **Разметка целевого диска (Diskpart):**
   * Полная очистка накопителя (`clean`).
   * Преобразование в разметку **GPT**.
   * Создание системного раздела EFI (`FAT32`, 100 МБ, метка `S:`).
   * Создание основного раздела Windows (`NTFS`, весь оставшийся объем, метка `W:`).
2. **Развертывание системы (DISM):**
   * Распаковка выбранного индекса редакции ОС из `.wim` на раздел `W:\`.
3. **Создание загрузчика UEFI (BCDBoot):**
   * Генерация системной конфигурации BCD на разделе `S:` для загрузки по стандарту UEFI.
4. **Оффлайн-модификация реестра:**
   * Подключение куста `reg load HKLM\OFFLINE_SOFT W:\Windows\System32\config\SOFTWARE`.
   * Применение выбранных политик безопасности и оптимизаций.
   * Сохранение изменений через `reg unload`.
5. **Генерация скрипта постобработки (`SetupComplete.cmd`):**
   * Создание сценария по пути `W:\Windows\Setup\Scripts\SetupComplete.cmd`.
   * Автопоиск буквы флешки через цикл `for %%D in (...)`.
   * Поочередная тихая установка софта с логированием в `C:\Windows\Temp\install.log`.
6. **Перезагрузка:**
   * Завершение среды Windows PE через команду `wpeutil reboot` и передача управления установленной ОС.

---

## 🛠 Ключи тихой установки компонентов

| Приложение | Категория | Ключи тихой установки |
| :--- | :--- | :--- |
| **Google Chrome** | Интернет и документы | `/silent /install` |
| **qBittorrent** | Интернет и документы | `/S` |
| **Adobe Acrobat Reader** | Интернет и документы | `/sAll /rs /msi EULA_ACCEPT=YES` |
| **Visual C++ AIO** | Системные компоненты | `/ai /gm2` |
| **DirectX Legacy** | Системные компоненты | `/Q /T:C:\Temp\DX_Extract` ➔ `DXSETUP.exe /silent` |
| **7-Zip** | Системные компоненты | `/S` |
| **Steam** | Игры и мультимедиа | `/S` |
| **VLC Player** | Игры и мультимедиа | `/L=1049 /S` |
| **NVIDIA App** | Игры и мультимедиа | `-s` |
| **MS Office LTSC** | Офисный пакет | `/S /PRO /ACT /RU` |
| **AIDA64** | Диагностика | `/SILENT` |
| **CrystalDiskInfo** | Диагностика | `/VERYSILENT` |
| **FurMark** | Диагностика | `/VERYSILENT` |

---

## 🧪 Тестирование (Unit Tests)

В решение включен проект модульных тестов **`WinPEInstallerWPF.Tests`** на платформе **xUnit**:

* **`GetInstallerArguments_ReturnsCorrectSilentFlags`**: проверка правильности сопоставления аргументов скрытого запуска для каждого приложения.
* **`CategorizeSoftware_CategorizesKnownToolsCorrectly`**: валидация классификации файлов по смысловым блокам интерфейса.
* **`BuildSetupCompleteScript_...`**: проверка синтаксиса командного файла, распаковщика DirectX, скрипта деинсталляции Copilot и вызова Office.

Запуск тестов в Visual Studio:
```text
Тест ➔ Запустить все тесты (Ctrl + R, A)
```

---

## 📦 Сборка релиза для загрузочной флешки

Для сборки автономного бинарника без внешних зависимостей выполните команду в консоли:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:UseAppHost=true
```

Скомпилированный файл будет находиться по пути:
```text
bin\Release\net10.0-windows\win-x64\publish\WinPEInstallerWPF.exe
```
