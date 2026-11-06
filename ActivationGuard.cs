using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace AgentPointer {
internal sealed class ActivationGuard : IDisposable {
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool ReadPointer(out Native.Point point);
    private static readonly object observerLock = new object();
    private static readonly List<IntPtr> observerModules = new List<IntPtr>();

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct ProcessEntry {
        internal uint Size, Usage, Pid;
        internal UIntPtr Heap;
        internal uint Module, Threads, Parent;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] internal string File;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { internal uint Size, Time; }

    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibrary(string file);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int kind, IntPtr callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);

    private IntPtr window, module, hook;
    private MemoryMappedFile mapping;
    private MemoryMappedViewAccessor view;
    private bool armed;
    private int pointerSequence;
    private ulong nonce;

    internal static bool TryReadSystemPointer(out Native.Point point) {
        lock(observerLock) {
            if(observerModules.Count != 0) {
                var address = GetProcAddress(observerModules[observerModules.Count - 1], "ReadSystemPointer");
                if(address != IntPtr.Zero)
                    return ((ReadPointer)Marshal.GetDelegateForFunctionPointer(address, typeof(ReadPointer)))(out point);
            }
        }
        point = new Native.Point();
        return false;
    }

    private static string LibraryPath {
        get {
            string manifest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AgentMouse.Guard.current");
            if(!File.Exists(manifest)) return null;
            string name = File.ReadAllText(manifest).Trim();
            if(!System.Text.RegularExpressions.Regex.IsMatch(name, @"^AgentMouse\.Guard\.[a-f0-9]{32}\.dll$")) return null;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "guards", name);
        }
    }
    internal static bool Available {
        get {
            var path = LibraryPath;
            return path != null && File.Exists(path);
        }
    }

    private long Send(string name) {
        UIntPtr result;
        uint message = RegisterWindowMessage("AgentMouse.Guard.v4." + name);
        if(Native.SendMessageTimeout(window, message, new UIntPtr(nonce), IntPtr.Zero, 2, 500, out result) == IntPtr.Zero)
            throw new Fault("guard_timeout", "Activation guard did not respond; no automatic input retry.");
        return (long)result.ToUInt64();
    }

    internal ActivationGuard(IntPtr window, bool processScope=false) {
        this.window = window;
        try {
            if(!Available)
                throw new Fault("guard_unavailable", "The activation guard DLL is missing. Reinstall the complete AgentMouse package.");
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(window, out pid);
            if(pid == 0 || (!processScope && (!Native.IsWindowVisible(window) || Native.IsIconic(window))))
                throw new Fault("window_changed", "Target must be a visible existing window.");
            var input = new LastInput { Size=8 };
            if(!GetLastInputInfo(ref input))
                throw new Fault("guard_unavailable", "Cannot read user input state.");
            long started;
            using(var process = Process.GetProcessById((int)pid))
                started = process.StartTime.ToUniversalTime().ToFileTimeUtc();

            var token=new byte[8];using(var random=System.Security.Cryptography.RandomNumberGenerator.Create())random.GetBytes(token);nonce=BitConverter.ToUInt64(token,0);
            mapping = MemoryMappedFile.CreateNew("Local\\AgentMouse.Guard.v4." + pid + "." + thread + "." + nonce.ToString("x16"), 80);
            view = mapping.CreateViewAccessor();
            // CONTROL v4 offsets are shared with native/activation-guard.c.
            view.Write(0, 0x414d4731u);
            view.Write(4, 4u);
            view.Write(8, pid);
            view.Write(12, thread);
            view.Write(16, (ulong)window.ToInt64());
            view.Write(24, started);
            view.Write(32, GetTickCount64() + 6500);
            view.Write(40, input.Time);
            view.Write(44, 1);
            view.Write(48, processScope ? 1u : 0u);
            view.Write(72,nonce);

            module = LoadLibrary(LibraryPath);
            if(module == IntPtr.Zero)
                throw new Fault("guard_unavailable", "Could not load activation guard.");
            lock(observerLock) observerModules.Add(module);
            var callback = GetProcAddress(module, "GuardMessages");
            if(callback == IntPtr.Zero)
                throw new Fault("guard_unavailable", "Guard entry point is missing.");
            hook = SetWindowsHookEx(4, callback, module, thread);
            if(hook == IntPtr.Zero)
                throw new Fault("guard_unavailable", "Cannot attach the target UI thread.");
            armed = true;
            Send("Begin");
            if(Send("Ready") != 1)
                throw new Fault("guard_unavailable", "Target did not acknowledge activation protection (native code " + GetProp(window, "AgentMouse.Guard.Error.v4").ToInt64() + "); no input sent.");
        } catch {
            Dispose();
            throw;
        }
    }

    internal object Receipt() {
        long value = Send("Stats");
        return new {
            activationBlocks=value & 255,
            focusBlocks=(value >> 8) & 255,
            positionBlocks=(value >> 16) & 255,
            virtualPointerQueries=Send("PointerStats")
        };
    }

    internal void UpdatePointer(Native.Point point, uint buttons) {
        view.Write(52, ++pointerSequence);
        view.Write(56, point.X);
        view.Write(60, point.Y);
        view.Write(64, buttons);
        view.Write(68, 1);
        view.Write(52, ++pointerSequence);
    }

    internal static Dictionary<uint,uint> ReadProcessParents() {
        var result = new Dictionary<uint,uint>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if(snapshot == new IntPtr(-1)) return null;
        try {
            var entry = new ProcessEntry { Size=(uint)Marshal.SizeOf(typeof(ProcessEntry)) };
            if(!Process32First(snapshot, ref entry)) return null;
            do { result[entry.Pid] = entry.Parent; } while(Process32Next(snapshot, ref entry));
            return result;
        } finally {
            CloseHandle(snapshot);
        }
    }

    internal static List<ActivationGuard> ProtectRelatedWindows(IntPtr mainWindow) {
        uint pid;
        uint mainThread = Native.GetWindowThreadProcessId(mainWindow, out pid);
        string windowClass = Native.Class(mainWindow);
        if(windowClass == "Shell_TrayWnd" || windowClass == "Progman" || windowClass == "WorkerW")
            return new List<ActivationGuard>();
        var parents = ReadProcessParents();
        if(parents == null)
            throw new Fault("guard_unavailable", "Cannot verify application process ancestry.");
        var family = new HashSet<uint> { pid };
        bool changed;
        do {
            changed = false;
            foreach(var entry in parents)
                if(family.Contains(entry.Value) && family.Add(entry.Key)) changed = true;
        } while(changed);

        var windows = new Dictionary<uint,IntPtr>();
        Native.EnumWindows(delegate(IntPtr candidate, IntPtr unused) {
            uint owner;
            uint thread = Native.GetWindowThreadProcessId(candidate, out owner);
            Native.Rect bounds;
            if(family.Contains(owner) && thread != mainThread && !windows.ContainsKey(thread) &&
                Native.GetWindowRect(candidate, out bounds) && bounds.Width >= 20 && bounds.Height >= 20)
                windows.Add(thread, candidate);
            return true;
        }, IntPtr.Zero);
        if(windows.Count > 32)
            throw new Fault("guard_unavailable", "Application has too many UI threads for a bounded protection transaction.");
        var result = new List<ActivationGuard>();
        try {
            foreach(var candidate in windows.Values) result.Add(new ActivationGuard(candidate, true));
            return result;
        } catch {
            foreach(var guard in result) guard.Dispose();
            throw;
        }
    }

    public void Dispose() {
        if(view != null) view.Write(44, 0);
        if(armed) {
            try { Send("End"); } catch(Fault) { }
            armed = false;
        }
        if(hook != IntPtr.Zero) {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
        if(module != IntPtr.Zero) {
            lock(observerLock) {
                observerModules.Remove(module);
                FreeLibrary(module);
                module = IntPtr.Zero;
            }
        }
        if(view != null) { view.Dispose(); view = null; }
        if(mapping != null) { mapping.Dispose(); mapping = null; }
    }
}
}
