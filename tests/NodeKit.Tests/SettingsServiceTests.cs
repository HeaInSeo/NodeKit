using System;
using System.IO;
using NodeKit.Settings;
using Xunit;

namespace NodeKit.Tests
{
    public class SettingsServiceTests
    {
        [Fact]
        public void Load_WhenFileMissing_ReturnsDefaultsAndNotCorrupted()
        {
            WithCleanSettingsFile(() =>
            {
                var settings = SettingsService.Load(out var wasCorrupted);

                Assert.False(wasCorrupted);
                Assert.Equal(new AppSettings().NodeVaultAddress, settings.NodeVaultAddress);
            });
        }

        [Fact]
        public void Load_WhenFileCorrupted_ReturnsDefaultsAndCorrupted()
        {
            WithCleanSettingsFile(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsService.FilePath)!);
                File.WriteAllText(SettingsService.FilePath, "{ not valid json");

                var settings = SettingsService.Load(out var wasCorrupted);

                Assert.True(wasCorrupted);
                Assert.Equal(new AppSettings().NodeVaultAddress, settings.NodeVaultAddress);
            });
        }

        [Fact]
        public void SaveThenLoad_RoundTripsValues()
        {
            WithCleanSettingsFile(() =>
            {
                var saved = new AppSettings { NodeVaultAddress = "http://test:1", CatalogAddress = "http://test:2" };
                SettingsService.Save(saved);

                var loaded = SettingsService.Load(out var wasCorrupted);

                Assert.False(wasCorrupted);
                Assert.Equal(saved.NodeVaultAddress, loaded.NodeVaultAddress);
                Assert.Equal(saved.CatalogAddress, loaded.CatalogAddress);
            });
        }

        [Fact]
        public void Save_DoesNotLeaveTempFileBehind()
        {
            WithCleanSettingsFile(() =>
            {
                SettingsService.Save(new AppSettings());

                var tempPath = SettingsService.FilePath + ".tmp";
                Assert.False(File.Exists(tempPath));
            });
        }

        /// <summary>
        /// SettingsService.FilePath는 고정된 OS 경로라 테스트 격리를 위해 실제 파일을
        /// 백업/복원한다. 사용자의 실제 설정 파일을 절대 잃지 않도록 try/finally로 복원한다.
        /// </summary>
        private static void WithCleanSettingsFile(Action action)
        {
            var path = SettingsService.FilePath;
            var backupPath = path + ".bak-test";
            var hadOriginal = File.Exists(path);
            if (hadOriginal)
            {
                File.Move(path, backupPath, overwrite: true);
            }

            try
            {
                action();
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                if (hadOriginal)
                {
                    File.Move(backupPath, path, overwrite: true);
                }
            }
        }
    }
}
