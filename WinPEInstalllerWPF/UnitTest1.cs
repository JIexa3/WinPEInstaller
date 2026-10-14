using System.Collections.Generic;
using Xunit;
using WinPEInstallerWPF;

namespace WinPEInstalllerWPF
{
    public class UnitTest1
    {
        [Theory]
        [InlineData("ChromeBrowser.exe", "/silent /install")]
        [InlineData("GoogleChrome_x64.exe", "/silent /install")]
        [InlineData("VisualC++.exe", "/ai /gm2")]
        [InlineData("vcredist_aio.exe", "/ai /gm2")]
        [InlineData("vlc-3.0.24-win64.exe", "/L=1049 /S")]
        [InlineData("NVIDIA_app_v11.0.9.565.exe", "-s")]
        [InlineData("Telegram.exe", "/VERYSILENT /NORESTART /SP-")]
        [InlineData("DiscordSetup.exe", "/s")]
        [InlineData("AcroRdrDC2600.exe", "/sAll /rs /msi EULA_ACCEPT=YES")]
        [InlineData("FurMark_2.1.0.2.exe", "/VERYSILENT")]
        [InlineData("CrystalDiskInfo9.exe", "/VERYSILENT")]
        [InlineData("aida64extreme8.exe", "/SILENT")]
        [InlineData("7zip.exe", "/S")]
        [InlineData("SteamSetup.exe", "/S")]
        public void GetInstallerArguments_ReturnsCorrectSilentFlags(string exeName, string expectedArgs)
        {
            string actualArgs = DeploymentScriptGenerator.GetInstallerArguments(exeName);
            Assert.Equal(expectedArgs, actualArgs);
        }

        [Fact]
        public void CategorizeSoftware_CategorizesKnownToolsCorrectly()
        {
            (string cat1, string name1) = DeploymentScriptGenerator.CategorizeSoftware("ChromeBrowser.exe");
            Assert.Equal("Интернет и документы", cat1);
            Assert.Equal("Google Chrome", name1);

            (string cat2, string name2) = DeploymentScriptGenerator.CategorizeSoftware("SteamSetup.exe");
            Assert.Equal("Игры и мультимедиа", cat2);
            Assert.Equal("Steam Client", name2);

            (string cat3, string name3) = DeploymentScriptGenerator.CategorizeSoftware("aida64extreme.exe");
            Assert.Equal("Диагностика и стресс-тесты", cat3);
            Assert.Equal("AIDA64 Extreme", name3);

            (string cat4, string name4) = DeploymentScriptGenerator.CategorizeSoftware("VisualC++.exe");
            Assert.Equal("Системные компоненты", cat4);
            Assert.Equal("Visual C++ 2005-2026", name4);
        }

        [Fact]
        public void BuildSetupCompleteScript_ContainsRequiredDirectXExtractionSequence()
        {
            var apps = new List<string> { "DirectX.exe" };
            string script = DeploymentScriptGenerator.BuildSetupCompleteScript(apps, installOffice: false, removeCopilot: false);

            Assert.Contains("mkdir C:\\Windows\\Temp\\DX_Extract", script);
            Assert.Contains("/Q /T:C:\\Windows\\Temp\\DX_Extract", script);
            Assert.Contains("DXSETUP.exe\" /silent", script);
            Assert.Contains("rmdir /s /q C:\\Windows\\Temp\\DX_Extract", script);
        }

        [Fact]
        public void BuildSetupCompleteScript_WhenOfficeSelected_GeneratesKpoJlukAutorunCall()
        {
            var apps = new List<string> { "7zip.exe" };
            string script = DeploymentScriptGenerator.BuildSetupCompleteScript(apps, installOffice: true, removeCopilot: false);

            Assert.Contains("AUTORUN.exe\" /S /PRO /ACT /RU", script);
        }

        [Fact]
        public void BuildSetupCompleteScript_WhenCopilotRemoved_GeneratesAppxRemovalCommands()
        {
            var apps = new List<string> { "7zip.exe" };
            string script = DeploymentScriptGenerator.BuildSetupCompleteScript(apps, installOffice: false, removeCopilot: true);

            Assert.Contains("Get-AppxPackage -AllUsers *Copilot* | Remove-AppxPackage", script);
        }
    }
}
