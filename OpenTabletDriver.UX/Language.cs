using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Eto.Forms;

namespace OpenTabletDriver.UX
{
    public static class Language
    {
        private static string PreferencePath => Path.Combine(
            OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenTabletDriver", "ui-language.txt");
        public static string Current { get; } = ReadPreference();
        // Eto controls are translated when constructed; the SwiftUI window can switch live.
        public static string Selected { get; private set; } = Current;
        public static void SaveSelection(string language)
        {
            if (language is not ("en" or "ru")) throw new ArgumentException("Unsupported language.", nameof(language));
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            string staged = PreferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(staged, language);
                File.Move(staged, PreferencePath, true);
                Selected = language;
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        private static readonly Dictionary<string, string> Russian = LoadRussian();

        private static string ReadPreference()
        {
            try { return File.ReadAllText(PreferencePath).Trim() == "ru" ? "ru" : "en"; }
            catch (IOException) { return "en"; }
            catch (UnauthorizedAccessException) { return "en"; }
        }
        private static Dictionary<string, string> LoadRussian()
        {
            using var stream = typeof(Language).Assembly.GetManifestResourceStream("OpenTabletDriver.UX.Localization.ru.json");
            return stream == null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        public static string T(string text) => Current == "ru" && Russian.TryGetValue(text, out var translated) ? translated : text;

        public static Button CreateButton()
        {
            var button = new Button { Text = T("Language") };
            var popup = new ContextMenu(CreateOptions());
            button.Click += (_, _) => popup.Show(button);
            return button;
        }

        public static ButtonMenuItem CreateMenu()
        {
            var menu = new ButtonMenuItem { Text = T("Language") };
            menu.Items.AddRange(CreateOptions());
            return menu;
        }

        private static IEnumerable<MenuItem> CreateOptions()
        {
            foreach (var option in new[] { ("en", "English"), ("ru", "Русский") })
            {
                var item = new CheckMenuItem { Text = option.Item2, Checked = Selected == option.Item1 };
                item.Click += (_, _) =>
                {
                    try
                    {
                        SaveSelection(option.Item1);
                        MessageBox.Show(T("The language will change after restarting OpenTabletDriver."), T("Language"));
                    }
                    catch (Exception ex) { MessageBox.Show(ex.Message, T("Application Error")); }
                };
                yield return item;
            }
        }
    }
}
