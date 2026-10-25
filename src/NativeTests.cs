using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MemePetsWindows
{
    public static class NativeTests
    {
        public static BitmapSource LoadFace(string root)
        {
            string path = Path.Combine(root, "Assets", "face.png");
            if (!File.Exists(path)) throw new FileNotFoundException("缺少透明头像 Assets/face.png，请重新解压完整桌宠。", path);
            try
            {
                var face = new BitmapImage(); face.BeginInit(); face.CacheOption = BitmapCacheOption.OnLoad;
                face.UriSource = new Uri(path, UriKind.Absolute); face.EndInit(); face.Freeze();
                if (face.PixelWidth < 1 || face.PixelHeight < 1 || face.PixelWidth > 8192 || face.PixelHeight > 8192)
                    throw new InvalidDataException("头像尺寸无效。");
                return face;
            }
            catch (Exception error) { throw new InvalidDataException("无法读取透明头像 face.png：" + error.Message, error); }
        }
        public static int Run(string root, Clip[] clips, string reportPath)
        {
            var results = new List<object>(); string failure = null;
            int transparentPixels = 0;
            try
            {
                BitmapSource face = LoadFace(root);
                var rgba = new FormatConvertedBitmap(face, PixelFormats.Bgra32, null, 0);
                int stride = rgba.PixelWidth * 4; byte[] pixels = new byte[stride * rgba.PixelHeight];
                rgba.CopyPixels(pixels, stride, 0);
                for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] < 255) transparentPixels++;
                if (transparentPixels == 0) throw new InvalidDataException("face.png 没有透明像素。");
                using (var voice = new VoicePlayer(Path.Combine(root, "Assets", "Playback")))
                {
                    voice.Volume = 0;
                    voice.Play(clips[0]); voice.Stop();
                    if (voice.IsBusy) throw new Exception("Stop did not release playback gate");
                    foreach (Clip clip in clips)
                    {
                        var wait = new DispatcherFrame(); double seconds = 0; int openedCount = 0; string reason = null;
                        Action<Clip, double> opened = delegate(Clip c, double length) { seconds = length; openedCount++; };
                        Action<Clip, string> finished = delegate(Clip c, string end) { reason = end; wait.Continue = false; };
                        voice.Opened += opened; voice.Finished += finished;
                        bool started = voice.Play(clip);
                        bool overlapBlocked = !voice.Play(clips[clip.id % clips.Length]);
                        if (reason == null) Dispatcher.PushFrame(wait);
                        voice.Opened -= opened; voice.Finished -= finished;
                        bool ok = started && overlapBlocked && openedCount == 1 && seconds > 0 && reason == "ended" && !voice.IsBusy;
                        results.Add(new { id = clip.id, file = clip.file, playback = Path.ChangeExtension(clip.file, ".wav"), ok = ok,
                            opened = openedCount, seconds = seconds, finished = reason, repeated_click_blocked = overlapBlocked });
                        if (!ok) throw new Exception("Native playback failed for " + clip.file + ": " + reason);
                    }
                }
            }
            catch (Exception error) { failure = error.Message; }
            File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(new {
                ok = failure == null, checked_clips = results.Count, silent_test_volume = 0, transparent_pixels = transparentPixels,
                error = failure, clips = results }), Encoding.UTF8);
            return failure == null ? 0 : 1;
        }
    }
}
