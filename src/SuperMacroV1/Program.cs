using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace SuperMacroV1;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\SuperMacroV1.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("SuperMacroV1 is already open.", "SuperMacroV1",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class AppSettings
{
    public int Cps { get; set; } = 12;
    public int MouseButton { get; set; }
    public string HotKey { get; set; } = "F6";
    public bool RequireCtrl { get; set; }
    public bool RequireAlt { get; set; }
    public bool RequireShift { get; set; }
    public bool StartMacroOnLaunch { get; set; }
    public bool GamesOnly { get; set; }
    public string Language { get; set; } = "en";
}

internal static class SettingsStore
{
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperMacroV1");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class RuntimeConfig
{
    public int MouseButton { get; init; }
    public bool FilterClients { get; init; }
}

internal sealed class MainForm : Form
{
    private readonly NumericUpDown _cps = new();
    private readonly ComboBox _mouseButton = new();
    private readonly ComboBox _hotKey = new();
    private readonly ComboBox _language = new();
    private readonly CheckBox _ctrl = new();
    private readonly CheckBox _alt = new();
    private readonly CheckBox _shift = new();
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _filterClients = new();
    private readonly Label _speedLabel = new();
    private readonly Label _speedHint = new();
    private readonly Label _buttonLabel = new();
    private readonly Label _hotKeyLabel = new();
    private readonly Label _languageLabel = new();
    private readonly Label _safety = new();
    private readonly Label _status = new();
    private readonly Button _toggleButton = new();
    private readonly Button _saveButton = new();
    private readonly System.Windows.Forms.Timer _keyTimer = new();
    private readonly System.Threading.Timer _clickTimer;
    private volatile bool _active;
    private volatile RuntimeConfig _runtime = new();
    private bool _lastHotKeyDown;
    private bool _lastEmergencyDown;
    private AppSettings _settings;

    private static readonly Color Background = Color.FromArgb(15, 16, 25);
    private static readonly Color Card = Color.FromArgb(28, 29, 43);
    private static readonly Color Field = Color.FromArgb(42, 43, 61);
    private static readonly Color Accent = Color.FromArgb(172, 76, 247);
    private static readonly Color Foreground = Color.FromArgb(247, 246, 252);
    private static readonly Color Muted = Color.FromArgb(173, 169, 190);
    private static readonly string[] LanguageCodes = { "en", "fr", "es" };
    private static readonly string[][] Copy =
    {
        new[] { "Click speed", "clicks per second (CPS)", "Mouse button", "Activation key", "Start clicking on launch", "Filter clients", "F12 · emergency stop", "START", "STOP", "SAVE SETTINGS", "● INACTIVE", "● ACTIVE", "● READY · WAITING FOR CLIENT", "Settings saved.", "Left click", "Right click", "Middle click" },
        new[] { "Vitesse de clic", "clics par seconde (CPS)", "Bouton de souris", "Touche d'activation", "Démarrer les clics à l'ouverture", "Filtrer les clients", "F12 · arrêt d'urgence", "DÉMARRER", "ARRÊTER", "ENREGISTRER", "● INACTIF", "● ACTIF", "● PRÊT · EN ATTENTE D'UN CLIENT", "Paramètres enregistrés.", "Clic gauche", "Clic droit", "Clic milieu" },
        new[] { "Velocidad de clic", "clics por segundo (CPS)", "Botón del ratón", "Tecla de activación", "Iniciar clics al abrir", "Filtrar clientes", "F12 · parada de emergencia", "INICIAR", "DETENER", "GUARDAR", "● INACTIVO", "● ACTIVO", "● LISTO · ESPERANDO CLIENTE", "Configuración guardada.", "Clic izquierdo", "Clic derecho", "Clic central" }
    };

    public MainForm()
    {
        _settings = SettingsStore.Load();
        Text = "SuperMacroV1";
        ClientSize = new Size(600, 548);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        using (Stream? iconStream = Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("SuperMacroV1.XT07.png"))
        {
            if (iconStream is not null)
            {
                using var bitmap = new Bitmap(iconStream);
                var logo = new PictureBox
                {
                    Image = new Bitmap(bitmap),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point(27, 20),
                    Size = new Size(78, 78),
                    BackColor = Color.Transparent
                };
                Controls.Add(logo);
            }
        }
        using (Stream? appIconStream = Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("SuperMacroV1.app.ico"))
        {
            if (appIconStream is not null) Icon = new Icon(appIconStream);
        }

        BuildInterface();
        LoadSettingsIntoControls();
        _clickTimer = new System.Threading.Timer(ClickTick, null, Timeout.Infinite, Timeout.Infinite);
        WireEvents();
        ApplyRuntimeSettings();
        ApplyLanguage();

        _keyTimer.Interval = 15;
        _keyTimer.Tick += PollKeys;
        _keyTimer.Start();
        Shown += (_, _) => { if (_settings.StartMacroOnLaunch) SetActive(true); };
        FormClosing += (_, _) =>
        {
            _keyTimer.Stop();
            _clickTimer.Dispose();
            SaveCurrentSettings();
        };
    }

    private void BuildInterface()
    {
        Controls.Add(new Label
        {
            Text = "SuperMacroV1", Font = new Font("Segoe UI Semibold", 25F),
            ForeColor = Foreground, AutoSize = true, Location = new Point(111, 21)
        });
        Controls.Add(new Label
        {
            Text = "By XT.07", ForeColor = Muted, AutoSize = true, Location = new Point(115, 71)
        });
        PlaceLabel(this, _languageLabel, 390, 40);
        SetupCombo(_language, 465, 34, 108);
        _language.Items.AddRange(new object[] { "English", "Français", "Español" });
        Controls.Add(_language);

        var card = new Panel
        {
            BackColor = Card, Location = new Point(25, 111), Size = new Size(550, 335)
        };
        Controls.Add(card);
        PlaceLabel(card, _speedLabel, 26, 27);
        _cps.Location = new Point(295, 22);
        _cps.Size = new Size(225, 31);
        _cps.Minimum = 1;
        _cps.Maximum = 100;
        _cps.TextAlign = HorizontalAlignment.Center;
        card.Controls.Add(_cps);
        PlaceLabel(card, _speedHint, 299, 56, true);

        PlaceLabel(card, _buttonLabel, 26, 91);
        SetupCombo(_mouseButton, 295, 85, 225);
        _mouseButton.Items.AddRange(new object[] { "Left click", "Right click", "Middle click" });
        card.Controls.Add(_mouseButton);

        PlaceLabel(card, _hotKeyLabel, 26, 151);
        SetupCombo(_hotKey, 295, 145, 225);
        _hotKey.Items.AddRange(new object[]
        {
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "Insert", "Home", "End", "PageUp", "PageDown", "XButton1", "XButton2"
        });
        card.Controls.Add(_hotKey);
        _ctrl.Text = "Ctrl";
        _alt.Text = "Alt";
        _shift.Text = "Shift";
        SetupCheckBox(_ctrl, 295, 184);
        SetupCheckBox(_alt, 365, 184);
        SetupCheckBox(_shift, 425, 184);
        card.Controls.AddRange(new Control[] { _ctrl, _alt, _shift });

        SetupCheckBox(_autoStart, 26, 232);
        _autoStart.AutoSize = true;
        card.Controls.Add(_autoStart);
        SetupCheckBox(_filterClients, 26, 269);
        _filterClients.AutoSize = true;
        card.Controls.Add(_filterClients);
        PlaceLabel(card, _safety, 26, 306, true);
        _safety.ForeColor = Color.FromArgb(255, 188, 103);

        SetupButton(_toggleButton, 25, 465, 270, Accent);
        Controls.Add(_toggleButton);
        SetupButton(_saveButton, 307, 465, 268, Field);
        Controls.Add(_saveButton);
        _status.Location = new Point(28, 522);
        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI Semibold", 9F);
        Controls.Add(_status);
    }

    private static void PlaceLabel(Control parent, Label label, int x, int y, bool small = false)
    {
        label.AutoSize = true;
        label.ForeColor = small ? Muted : Foreground;
        if (small) label.Font = new Font("Segoe UI", 8.5F);
        label.Location = new Point(x, y);
        parent.Controls.Add(label);
    }

    private static void SetupCombo(ComboBox combo, int x, int y, int width)
    {
        combo.Location = new Point(x, y);
        combo.Size = new Size(width, 32);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Field;
        combo.ForeColor = Foreground;
    }

    private static void SetupCheckBox(CheckBox checkbox, int x, int y)
    {
        checkbox.Location = new Point(x, y);
        checkbox.ForeColor = Foreground;
        checkbox.BackColor = Color.Transparent;
    }

    private static void SetupButton(Button button, int x, int y, int width, Color color)
    {
        button.Location = new Point(x, y);
        button.Size = new Size(width, 48);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Foreground;
        button.Font = new Font("Segoe UI Semibold", 10F);
        button.Cursor = Cursors.Hand;
    }

    private void LoadSettingsIntoControls()
    {
        _cps.Value = Math.Clamp(_settings.Cps, 1, 100);
        _mouseButton.SelectedIndex = Math.Clamp(_settings.MouseButton, 0, 2);
        _hotKey.SelectedItem = _settings.HotKey;
        if (_hotKey.SelectedIndex < 0) _hotKey.SelectedItem = "F6";
        _ctrl.Checked = _settings.RequireCtrl;
        _alt.Checked = _settings.RequireAlt;
        _shift.Checked = _settings.RequireShift;
        _autoStart.Checked = _settings.StartMacroOnLaunch;
        _filterClients.Checked = _settings.GamesOnly;
        int languageIndex = Array.IndexOf(LanguageCodes, _settings.Language);
        _language.SelectedIndex = languageIndex < 0 ? 0 : languageIndex;
    }

    private void WireEvents()
    {
        _toggleButton.Click += (_, _) => SetActive(!_active);
        _saveButton.Click += (_, _) =>
        {
            SaveCurrentSettings();
            MessageBox.Show(Copy[_language.SelectedIndex][13], "SuperMacroV1",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _language.SelectedIndexChanged += (_, _) =>
        {
            ApplyLanguage();
            SaveCurrentSettings();
        };
        _cps.ValueChanged += (_, _) => ApplyRuntimeSettings();
        _mouseButton.SelectedIndexChanged += (_, _) => ApplyRuntimeSettings();
        _filterClients.CheckedChanged += (_, _) => { ApplyRuntimeSettings(); RefreshStatus(); };
    }

    private void ApplyLanguage()
    {
        int index = Math.Max(0, _language.SelectedIndex);
        string[] words = Copy[index];
        _languageLabel.Text = index switch { 1 => "Langue", 2 => "Idioma", _ => "Language" };
        _speedLabel.Text = words[0];
        _speedHint.Text = words[1];
        _buttonLabel.Text = words[2];
        _hotKeyLabel.Text = words[3];
        _autoStart.Text = words[4];
        _filterClients.Text = words[5];
        _safety.Text = words[6];
        _shift.Text = index == 1 ? "Maj" : index == 2 ? "Mayús" : "Shift";
        int selectedButton = Math.Max(0, _mouseButton.SelectedIndex);
        _mouseButton.Items.Clear();
        _mouseButton.Items.AddRange(new object[] { words[14], words[15], words[16] });
        _mouseButton.SelectedIndex = selectedButton;
        _saveButton.Text = words[9];
        RefreshStatus();
    }

    private void ApplyRuntimeSettings()
    {
        _runtime = new RuntimeConfig
        {
            MouseButton = Math.Max(0, _mouseButton.SelectedIndex),
            FilterClients = _filterClients.Checked
        };
        int interval = Math.Max(1, (int)Math.Round(1000.0 / (double)_cps.Value));
        _clickTimer?.Change(0, interval);
    }

    private void PollKeys(object? sender, EventArgs e)
    {
        bool emergencyDown = NativeMethods.IsKeyDown(Keys.F12);
        if (emergencyDown && !_lastEmergencyDown) SetActive(false);
        _lastEmergencyDown = emergencyDown;

        Keys key = Enum.TryParse(_hotKey.SelectedItem?.ToString(), out Keys parsed) ? parsed : Keys.F6;
        bool hotKeyDown = NativeMethods.IsKeyDown(key) &&
            (!_ctrl.Checked || NativeMethods.IsKeyDown(Keys.ControlKey)) &&
            (!_alt.Checked || NativeMethods.IsKeyDown(Keys.Menu)) &&
            (!_shift.Checked || NativeMethods.IsKeyDown(Keys.ShiftKey));
        if (hotKeyDown && !_lastHotKeyDown && !emergencyDown) SetActive(!_active);
        _lastHotKeyDown = hotKeyDown;
        RefreshStatus();
    }

    private void SetActive(bool value)
    {
        _active = value;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        string[] words = Copy[Math.Max(0, _language.SelectedIndex)];
        if (!_active)
        {
            _status.Text = words[10];
            _status.ForeColor = Muted;
        }
        else if (_runtime.FilterClients && !NativeMethods.IsSupportedClientForeground())
        {
            _status.Text = words[12];
            _status.ForeColor = Color.FromArgb(255, 188, 103);
        }
        else
        {
            _status.Text = words[11];
            _status.ForeColor = Color.FromArgb(92, 222, 166);
        }
        _toggleButton.Text = _active ? words[8] : words[7];
        _toggleButton.BackColor = _active ? Color.FromArgb(213, 71, 112) : Accent;
    }

    private void ClickTick(object? state)
    {
        if (!_active) return;
        RuntimeConfig config = _runtime;
        if (config.FilterClients && !NativeMethods.IsSupportedClientForeground()) return;
        NativeMethods.SendMouseClick(config.MouseButton);
    }

    private void SaveCurrentSettings()
    {
        _settings = new AppSettings
        {
            Cps = (int)_cps.Value,
            MouseButton = Math.Max(0, _mouseButton.SelectedIndex),
            HotKey = _hotKey.SelectedItem?.ToString() ?? "F6",
            RequireCtrl = _ctrl.Checked,
            RequireAlt = _alt.Checked,
            RequireShift = _shift.Checked,
            StartMacroOnLaunch = _autoStart.Checked,
            GamesOnly = _filterClients.Checked,
            Language = LanguageCodes[Math.Max(0, _language.SelectedIndex)]
        };
        SettingsStore.Save(_settings);
    }
}

internal static class NativeMethods
{
    private const uint InputMouse = 0;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint MiddleDown = 0x0020;
    private const uint MiddleUp = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInput);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    public static bool IsKeyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    public static void SendMouseClick(int button)
    {
        (uint down, uint up) = button switch
        {
            1 => (RightDown, RightUp),
            2 => (MiddleDown, MiddleUp),
            _ => (LeftDown, LeftUp)
        };
        var inputs = new[]
        {
            new Input { Type = InputMouse, Mouse = new MouseInput { Flags = down } },
            new Input { Type = InputMouse, Mouse = new MouseInput { Flags = up } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static readonly string[] SupportedProcessMarkers =
    {
        "minecraft", "javaw", "lunar", "cmclient", "badlion", "feather", "labymod",
        "salwyrr", "prism", "multimc", "curseforge", "modrinth", "sklauncher",
        "tlauncher", "gdlauncher", "atlauncher", "technic", "ftb", "robloxplayerbeta"
    };
    private static readonly string[] SupportedTitleMarkers =
    {
        "minecraft", "lunar client", "cmclient", "badlion", "feather client",
        "labymod", "salwyrr", "prism launcher", "multimc", "curseforge",
        "modrinth", "fabric", "forge", "quilt", "optifine", "roblox"
    };

    public static bool IsSupportedClientForeground()
    {
        try
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            var titleBuilder = new StringBuilder(512);
            GetWindowText(window, titleBuilder, titleBuilder.Capacity);
            GetWindowThreadProcessId(window, out uint processId);
            string process = Process.GetProcessById((int)processId).ProcessName;
            return SupportedProcessMarkers.Any(marker => process.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
                   SupportedTitleMarkers.Any(marker => titleBuilder.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
