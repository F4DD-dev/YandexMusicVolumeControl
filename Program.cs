using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using System.Drawing;

class Program : ApplicationContext
{
    NotifyIcon trayIcon;
    static Config cfg;
    static string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    AudioSessionControl targetSession;
    SimpleAudioVolume volume;
    bool wasInc = false, wasDec = false, wasMute = false;

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    const int VK_F10 = 0x79;

    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    const int SW_HIDE    = 0;
    const int SW_RESTORE = 9;

    IntPtr consoleHandle;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Program());
    }

    public Program()
    {
        consoleHandle = GetConsoleWindow();

        Console.WriteLine("MADE BY F4DD (https://github.com/F4DD-dev)\n");
        LoadOrCreateConfig();
        Console.WriteLine("Yandex Music Volume Controller");
        Console.WriteLine($"Hotkeys: Increase: {cfg.IncreaseKey}, Decrease: {cfg.DecreaseKey}, Mute: {cfg.MuteKey}");
        Console.WriteLine("F10 to configure keys, close console window to exit.\n");

        var mm = new MMDeviceEnumerator();
        var device = mm.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.GetProcessID == 0) continue;
            try
            {
                var p = Process.GetProcessById((int)s.GetProcessID);
                string procName = p.ProcessName.ToLower().Replace(" ", "");
                if (procName.Contains("yandexmusic") || procName.Contains("яндексмузыка"))
                {
                    targetSession = s;
                    break;
                }
            }
            catch { }
        }
        if (targetSession == null)
        {
            Console.WriteLine("Yandex Music process not found. Start it and re-run.");
            Environment.Exit(0);
        }
        volume = targetSession.SimpleAudioVolume;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Configure Hotkeys", null, (s, e) => Reconfigure());
        menu.Items.Add("Show Console",     null, (s, e) => ShowConsole());
        menu.Items.Add("Exit",             null, (s, e) => Exit());

        trayIcon = new NotifyIcon()
        {
            Icon             = LoadIconFromResource("YandexMusicVolumeControl.app.ico"),
            Text             = "Yandex Music Volume Controller",
            Visible          = true,
            ContextMenuStrip = menu
        };
        trayIcon.MouseDoubleClick += (s, e) => ShowConsole();

        Console.CancelKeyPress += (s, e) => Exit();

        new Thread(Loop) { IsBackground = true }.Start();

        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(200);
                if (IsIconic(consoleHandle))
                    ShowWindow(consoleHandle, SW_HIDE);
            }
        })
        { IsBackground = true }.Start();

        Application.ApplicationExit += (s, e) => trayIcon.Visible = false;
    }

    private Icon LoadIconFromResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException("Resource not found: " + resourceName);
        return new Icon(stream);
    }

    void Loop()
    {
        while (true)
        {
            Thread.Sleep(50);
            if ((GetAsyncKeyState(VK_F10) & 0x8000) != 0)
            {
                trayIcon.ShowBalloonTip(2000, "Config", "Opening configuration...", ToolTipIcon.Info);
                Reconfigure();
            }

            HandleKey(cfg.IncreaseKey, ref wasInc, () => Adjust(volume, +0.1f));
            HandleKey(cfg.DecreaseKey, ref wasDec, () => Adjust(volume, -0.1f));
            HandleKey(cfg.MuteKey,     ref wasMute, () => ToggleMute(volume));
        }
    }

    void HandleKey(Keys key, ref bool wasPressed, Action action)
    {
        bool isPressed = (GetAsyncKeyState((int)key) & 0x8000) != 0;
        if (isPressed && !wasPressed)
            action();
        wasPressed = isPressed;
    }

    void Adjust(SimpleAudioVolume vol, float delta)
    {
        float newVol = Math.Clamp(vol.Volume + delta, 0f, 1f);
        vol.Volume = newVol;
        trayIcon.ShowBalloonTip(500, "Volume Changed", $"Volume: {(int)(newVol * 100)}%", ToolTipIcon.Info);
        Console.WriteLine($"Volume: {(int)(newVol * 100)}%");
    }

    void ToggleMute(SimpleAudioVolume vol)
    {
        vol.Mute = !vol.Mute;
        trayIcon.ShowBalloonTip(500, "Mute Toggled", vol.Mute ? "Muted" : "Unmuted", ToolTipIcon.Info);
        Console.WriteLine(vol.Mute ? "Muted" : "Unmuted");
    }

    void LoadOrCreateConfig()
    {
        if (File.Exists(configPath))
        {
            try
            {
                cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath));
                return;
            }
            catch { }
        }
        Reconfigure();
    }

    void Reconfigure()
    {
        while ((GetAsyncKeyState(VK_F10) & 0x8000) != 0)
            Thread.Sleep(50);
        while (Console.KeyAvailable)
            Console.ReadKey(true);

        Console.WriteLine("Configurate.");

        Console.WriteLine("Press VolumeUP Hotkey");
        var incKeyInfo = Console.ReadKey(true);
        var ik = (Keys)incKeyInfo.Key;
        Console.WriteLine($"Selected: {ik}");

        Console.WriteLine("Press VolumeDOWN Hotkey");
        var decKeyInfo = Console.ReadKey(true);
        var dk = (Keys)decKeyInfo.Key;
        Console.WriteLine($"Selected: {dk}");

        Console.WriteLine("Press Mute/Unmute Hotkey");
        var muteKeyInfo = Console.ReadKey(true);
        var mk = (Keys)muteKeyInfo.Key;
        Console.WriteLine($"Selected: {mk}");

        cfg = new Config { IncreaseKey = ik, DecreaseKey = dk, MuteKey = mk };
        File.WriteAllText(configPath,
            JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Done! Restart for apply");
    }

    void ShowConsole()
    {
        ShowWindow(consoleHandle, SW_RESTORE);
        SetForegroundWindow(consoleHandle);
    }

    void Exit()
    {
        trayIcon.Visible = false;
        Environment.Exit(0);
    }
}

class Config
{
    public Keys IncreaseKey  { get; set; }
    public Keys DecreaseKey  { get; set; }
    public Keys MuteKey      { get; set; }
}
