namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // アプリ設定クラス
        class AppSettings
        {
            public string UiCulture { get; set; } = "ja";
            public bool ExportConstantSpeed { get; set; } = false;
            public bool EnableSva { get; set; } = false;
        }
        AppSettings appSettings;


        // 読み書きヘルパー
        static string SettingsDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OsuStdToTaikoGui");

        // 設定ファイルパス
        static string SettingsPath =>
            Path.Combine(SettingsDir, "settings.json");

        // 読み込み（失敗したらデフォルト設定を返す）
        static AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                var json = File.ReadAllText(SettingsPath);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)
                       ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        // 保存（失敗しても無視）
        static void SaveSettings(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = System.Text.Json.JsonSerializer.Serialize(
                    settings,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 保存失敗は無視（設定なので）
            }
        }
    }
}
