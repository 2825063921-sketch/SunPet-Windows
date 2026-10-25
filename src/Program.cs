using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;

namespace MemePetsWindows
{
    public sealed class PetConfig
    {
        public string role { get; set; }
        public string title { get; set; }
        public string exe { get; set; }
        public int clipCount { get; set; }
        public int[] defaultIds { get; set; }
        public static string TitleFor(string role)
        { return role == "sun" ? "SunPet · Windows" : role == "shaolan" ? "薄肌桌宠 · Windows" : "好事桌宠 · Windows"; }
        public static PetConfig Load(string root)
        {
            var config = new JavaScriptSerializer().Deserialize<PetConfig>(File.ReadAllText(Path.Combine(root, "pet.json"), Encoding.UTF8));
            if (config == null || !LinkRules.IsRole(config.role)) throw new InvalidDataException("pet.json 角色配置无效。");
            int count = config.role == "sun" ? 28 : config.role == "shaolan" ? 37 : 11;
            string executable = config.role == "sun" ? "SunPet.exe" : config.role == "shaolan" ? "ShaolanPet.exe" : "FengGe.exe";
            if (config.clipCount != count || config.title != TitleFor(config.role) || config.exe != executable ||
                config.defaultIds == null || config.defaultIds.Length == 0 || config.defaultIds.Distinct().Count() != config.defaultIds.Length ||
                config.defaultIds.Any(id => id < 1 || id > count)) throw new InvalidDataException("pet.json 数量、标题或默认音频配置无效。");
            return config;
        }
    }
    public static class Program
    {
        public static bool SafeFileName(string name)
        {
            return !String.IsNullOrWhiteSpace(name) && name != "." && name != ".." &&
                name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOf('/') < 0 && name.IndexOf('\\') < 0 &&
                Path.GetFileName(name) == name && !Path.IsPathRooted(name);
        }
        public static Clip[] LoadClips(string root, PetConfig config)
        {
            var clips = new JavaScriptSerializer().Deserialize<Clip[]>(File.ReadAllText(Path.Combine(root, "Assets", "clips.json"), Encoding.UTF8));
            if (clips == null || clips.Length != config.clipCount || clips.Any(c => c == null) ||
                clips.Select(c => c.id).Distinct().Count() != config.clipCount ||
                clips.Select(c => c.file).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.clipCount ||
                clips.Select(c => Path.ChangeExtension(c.file, ".wav")).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.clipCount)
                throw new InvalidDataException("原声清单数量不符或存在重复 ID / 文件。");
            foreach (Clip clip in clips)
                if (clip.id < 1 || clip.id > config.clipCount || String.IsNullOrWhiteSpace(clip.label) || !SafeFileName(clip.file) ||
                    Double.IsNaN(clip.duration) || Double.IsInfinity(clip.duration) || clip.duration <= 0 || clip.duration > 600 ||
                    !File.Exists(Path.Combine(root, "Assets", "Clips", clip.file)) ||
                    !File.Exists(Path.Combine(root, "Assets", "Playback", Path.ChangeExtension(clip.file, ".wav"))))
                    throw new InvalidDataException("原声音频缺失或清单无效：" + clip.id);
            return clips.OrderBy(c => c.id).ToArray();
        }
        [STAThread]
        public static int Main(string[] args)
        {
            bool testing = args.Length == 2 && args[0] == "--self-test";
            bool diagnostics = args.Length == 1 && args[0] == "--diagnostics";
            try
            {
                if (args.Length != 0 && !testing && !diagnostics) throw new ArgumentException("参数无效。支持 --diagnostics 或 --self-test <报告绝对路径>。");
                if (testing && !Path.IsPathRooted(args[1])) throw new ArgumentException("自测报告必须使用绝对路径。");
                string root = AppDomain.CurrentDomain.BaseDirectory;
                PetConfig config = PetConfig.Load(root);
                Clip[] clips = LoadClips(root, config);
                if (testing) return NativeTests.Run(root, clips, args[1]);
                bool first;
                using (var mutex = new Mutex(true, @"Local\MemePetsWindows-" + config.role, out first))
                {
                    if (!first) { MessageBox.Show(config.title + " 已在运行。右键桌宠可以选句或退出。", config.title); return 0; }
                    try { return new Application().Run(new PetWindow(root, config, clips, diagnostics)); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                if (testing && Path.IsPathRooted(args[1]))
                {
                    try { File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new { ok = false, checked_clips = 0, clips = new object[0], error = error.Message }), Encoding.UTF8); }
                    catch (Exception reportError) { MessageBox.Show("无法写入自测报告：" + reportError.Message + "\n" + error.Message, "桌宠自测失败"); }
                }
                else MessageBox.Show(error.Message, "桌宠无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
