using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;

namespace MemePetsWindows
{
    public sealed class Diagnostics
    {
        readonly string role;
        readonly bool enabled;
        public string DirectoryPath { get; private set; }
        bool warned;
        public Diagnostics(string role, bool enabled)
        {
            this.role = role; this.enabled = enabled;
            DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemePetsWindows", role);
        }
        public void Write(string name, LinkMessage context, int? clipId, string reason)
        {
            if (!enabled) return;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string line = new JavaScriptSerializer().Serialize(new {
                    @event = name, role = role, root = context == null ? null : context.root,
                    hop = context == null ? (int?)null : context.hop,
                    chainId = context == null ? null : context.chainId, clipId = clipId, reason = reason, utc = DateTime.UtcNow.ToString("o") });
                File.AppendAllText(Path.Combine(DirectoryPath, "diagnostics.jsonl"), line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception error)
            {
                if (!warned) { warned = true; MessageBox.Show("无法写入诊断记录：" + error.Message, "桌宠诊断", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }
    }

    public sealed class Bridge : IDisposable
    {
        const int CopyData = 0x004A;
        const long Protocol = 0x4D505731;
        readonly Window window;
        readonly Diagnostics diagnostics;
        HwndSource source;
        IntPtr handle;
        public event Action<LinkMessage> Received;
        [StructLayout(LayoutKind.Sequential)]
        struct CopyDataStruct { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, ref CopyDataStruct lParam, uint flags, uint timeout, out IntPtr result);
        public Bridge(Window window, Diagnostics diagnostics)
        {
            this.window = window; this.diagnostics = diagnostics;
            window.SourceInitialized += OnInitialized;
        }
        void OnInitialized(object sender, EventArgs args)
        {
            handle = new WindowInteropHelper(window).Handle;
            source = HwndSource.FromHwnd(handle); source.AddHook(Hook);
        }
        IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != CopyData || lParam == IntPtr.Zero) return IntPtr.Zero;
            var data = (CopyDataStruct)Marshal.PtrToStructure(lParam, typeof(CopyDataStruct));
            if (data.dwData.ToInt64() != Protocol) return IntPtr.Zero;
            handled = true;
            if (data.cbData < 4 || data.cbData > 4096 || data.cbData % 2 != 0 || data.lpData == IntPtr.Zero)
            { diagnostics.Write("drop", null, null, "payload-size"); return IntPtr.Zero; }
            try
            {
                string json = Marshal.PtrToStringUni(data.lpData, data.cbData / 2).TrimEnd('\0');
                var serializer = new JavaScriptSerializer { MaxJsonLength = 2048, RecursionLimit = 8 };
                var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (fields == null || fields.Count != 5 || !fields.ContainsKey("from") || !(fields["from"] is string) ||
                    !fields.ContainsKey("root") || !(fields["root"] is string) || !fields.ContainsKey("hop") || !(fields["hop"] is int) ||
                    !fields.ContainsKey("chainId") || !(fields["chainId"] is string) || !fields.ContainsKey("utc") || !(fields["utc"] is string))
                { diagnostics.Write("drop", null, null, "payload-fields"); return IntPtr.Zero; }
                var context = serializer.Deserialize<LinkMessage>(json);
                if (Received != null) Received(context);
                return new IntPtr(1);
            }
            catch (Exception error) { diagnostics.Write("drop", null, null, "payload:" + error.GetType().Name); return IntPtr.Zero; }
        }
        public void Send(LinkMessage context, int clipId)
        {
            string reason;
            if (!LinkRules.Validate(context, DateTime.UtcNow, out reason))
            { diagnostics.Write("drop", context, clipId, reason); return; }
            foreach (string target in LinkRules.Targets(context))
            {
                IntPtr destination = FindWindow(null, PetConfig.TitleFor(target));
                if (destination == IntPtr.Zero) { diagnostics.Write("drop", context, clipId, "target-absent:" + target); continue; }
                string json = new JavaScriptSerializer().Serialize(context);
                IntPtr buffer = Marshal.StringToHGlobalUni(json);
                try
                {
                    var data = new CopyDataStruct { dwData = new IntPtr(Protocol), cbData = (json.Length + 1) * 2, lpData = buffer };
                    IntPtr response;
                    IntPtr sent = SendMessageTimeout(destination, CopyData, handle, ref data, 3, 200, out response);
                    diagnostics.Write(sent == IntPtr.Zero || response == IntPtr.Zero ? "drop" : "send", context, clipId,
                        sent == IntPtr.Zero ? "target-timeout:" + target : response == IntPtr.Zero ? "target-rejected:" + target : target);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        public void Dispose()
        {
            window.SourceInitialized -= OnInitialized;
            if (source != null) { source.RemoveHook(Hook); source = null; }
            Received = null;
        }
    }
}
