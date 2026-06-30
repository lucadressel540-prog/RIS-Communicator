using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RisDataEditor;

public partial class Form1 : Form
{
    private static readonly string[] DbScreenSansFamilies = ["DBScreenSans", "DB Screen Sans", "DB Sans", "Segoe UI"];
    private static readonly string[] DbScreenHeadFamilies = ["DB ScreenHead", "DBScreenHead", "DB Screen Head", "DB Sans", "Segoe UI"];
    private static readonly Color AppBackground = Color.FromArgb(238, 243, 246);
    private static readonly Color Surface = Color.White;
    private static readonly Color FieldBackground = Color.FromArgb(248, 250, 252);
    private static readonly Color TextStrong = Color.FromArgb(31, 35, 45);
    private static readonly Color TextMuted = Color.FromArgb(99, 104, 114);
    private static readonly Color DbRed = Color.FromArgb(224, 0, 26);
    private static readonly Color Border = Color.FromArgb(218, 222, 226);

    private readonly BindingList<TrainRecord> _trains = [];
    private readonly BindingList<StopRecord> _stops = [];
    private readonly BindingList<StationOption> _stationOptions = [];
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5068"),
        Timeout = TimeSpan.FromSeconds(12)
    };
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private ListBox _trainList = null!;
    private TextBox _numberBox = null!;
    private TextBox _lineBox = null!;
    private TextBox _fromBox = null!;
    private TextBox _toBox = null!;
    private TextBox _durationBox = null!;
    private TextBox _operatorBox = null!;
    private TextBox _trainFilterBox = null!;
    private ComboBox _operatorFilterBox = null!;
    private DataGridView _stopsGrid = null!;
    private TextBox _stationSearchBox = null!;
    private ComboBox _stationCombo = null!;
    private Label _statusLabel = null!;
    private string _dataFilePath = "";
    private string _databaseFilePath = "";
    private RisDatabase _database = null!;
    private readonly LoginUser _currentUser;
    private TrainRecord? _loadedTrain;
    private bool _loadingSelection;

    public Form1(LoginUser? currentUser = null)
    {
        _currentUser = currentUser ?? new LoginUser("", "", "");
        InitializeComponent();
        BuildEditorUi();

        var dataDirectory = AppPaths.LocateDataDirectory();
        _dataFilePath = Path.Combine(dataDirectory, "trains.json");
        _databaseFilePath = Path.Combine(dataDirectory, "ris-editor.db");
        _database = new RisDatabase(_databaseFilePath);
        _database.Initialize();
        ImportJsonIfDatabaseIsEmpty();
        LoadData();
    }

    private void BuildEditorUi()
    {
        Text = "RIS-Communicator Backoffice - INTERN";
        MinimumSize = new Size(1180, 760);
        Size = new Size(1360, 840);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackground;
        Font = DbScreenSans(10F);
        ApplyWindowIcon();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = Padding.Empty,
            BackColor = AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        root.Controls.Add(CreateHeader(), 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(22),
            BackColor = AppBackground
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 338));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        var left = CreateSurfacePanel(4, new Padding(0));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
        body.Controls.Add(left, 0, 0);

        left.Controls.Add(new Label
        {
            Text = "Navigation",
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 18, 0),
            Font = new Font("DB Sans", 15F, FontStyle.Bold),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var navPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = Surface,
            Padding = new Padding(14, 0, 14, 8)
        };
        navPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        navPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        navPanel.Controls.Add(CreateNavigationItem("RIS Fahrpläne", true), 0, 0);
        navPanel.Controls.Add(CreateNavigationItem("Rollen / Anmeldedaten", false, OpenCredentials), 0, 1);
        left.Controls.Add(navPanel, 0, 1);

        var listShell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            Padding = new Padding(14, 8, 14, 0),
            BackColor = Surface
        };
        listShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        listShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        listShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        listShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listShell.Controls.Add(new Label
        {
            Text = "Alle Gespeicherte Züge:",
            Dock = DockStyle.Fill,
            Font = new Font("DB Sans", 10.5F, FontStyle.Bold),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _trainFilterBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = FieldBackground,
            ForeColor = TextStrong,
            Font = DbScreenSans(10F),
            PlaceholderText = "Zug, Halt oder Unternehmen suchen"
        };
        _trainFilterBox.TextChanged += (_, _) => ApplyTrainFilter();
        listShell.Controls.Add(_trainFilterBox, 0, 1);

        _operatorFilterBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = FieldBackground,
            ForeColor = TextStrong,
            Font = DbScreenSans(10F)
        };
        _operatorFilterBox.SelectedIndexChanged += OperatorFilterChanged;
        listShell.Controls.Add(_operatorFilterBox, 0, 2);

        _trainList = new ListBox
        {
            Dock = DockStyle.Fill,
            DataSource = _trains,
            DisplayMember = nameof(TrainRecord.DisplayName),
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            BackColor = FieldBackground,
            ForeColor = TextStrong,
            ItemHeight = 28
        };
        _trainList.SelectedIndexChanged += (_, _) => LoadSelectedTrain();
        listShell.Controls.Add(_trainList, 0, 3);
        left.Controls.Add(listShell, 0, 2);

        var trainButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14, 14, 14, 0),
            BackColor = Surface
        };
        trainButtons.Controls.Add(MakeButton("Neuen Zug anlegen", AddTrain, 296, primary: true));
        trainButtons.Controls.Add(MakeButton("Ausgewählten Zug kopieren", CopySelectedTrain, 296));
        trainButtons.Controls.Add(MakeButton("Ausgewählten Zug löschen", DeleteTrain, 296));
        trainButtons.Controls.Add(MakeButton("JSON-Daten importieren", ImportJsonFromDialog, 296));
        trainButtons.Controls.Add(MakeButton("Rollen und Benutzer", OpenCredentials, 296));
        left.Controls.Add(trainButtons, 0, 3);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(20, 0, 0, 0),
            BackColor = AppBackground
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 164));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        body.Controls.Add(right, 1, 0);

        var pageTitle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            BackColor = AppBackground,
            Padding = new Padding(0, 0, 0, 10)
        };
        pageTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        pageTitle.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        pageTitle.Controls.Add(new Label
        {
            Text = "Neuer RIS Fahrplan",
            Dock = DockStyle.Fill,
            Font = new Font("DB Sans", 20F, FontStyle.Bold),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        pageTitle.Controls.Add(new Label
        {
            Text = "Hier können Sie einen neuen RIS Fahrplan anlegen, oder einen RIS Fahrplan aus der Lokalen Datenbank bearbeiten!",
            Dock = DockStyle.Fill,
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        right.Controls.Add(pageTitle, 0, 0);

        right.Controls.Add(CreateSectionLabel("Stammdaten"), 0, 1);

        var fields = CreateSurfacePanel(2, new Padding(20, 16, 20, 14));
        fields.ColumnCount = 4;
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        right.Controls.Add(fields, 0, 2);

        _numberBox = AddField(fields, "Zugnummer", 0, 0);
        _lineBox = AddField(fields, "Linie", 1, 0);
        _fromBox = AddField(fields, "Starthalt", 2, 0);
        _toBox = AddField(fields, "Endhalt", 3, 0);
        _durationBox = AddField(fields, "Fahrtdauer", 0, 1);
        _operatorBox = AddField(fields, "Verkehrsunternehmen", 1, 1);

        right.Controls.Add(CreateStationPicker(), 0, 3);

        var stopTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = AppBackground,
            Padding = new Padding(0, 16, 0, 0)
        };
        stopTools.Controls.Add(new Label
        {
            Text = "Halte",
            AutoSize = false,
            Width = 70,
            Height = 32,
            Font = new Font("DBScreenSans", 11F, FontStyle.Bold),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        });
        stopTools.Controls.Add(MakeButton("Halt hinzufügen", AddStop, 150));
        stopTools.Controls.Add(MakeButton("Halt löschen", DeleteStop, 130));
        stopTools.Controls.Add(MakeButton("Fahrtdauer berechnen", CalculateDuration, 155));
        right.Controls.Add(stopTools, 0, 4);

        _stopsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            DataSource = _stops,
            RowHeadersWidth = 34,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Surface,
            GridColor = Border,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            RowTemplate = { Height = 30 },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(242, 245, 248),
                ForeColor = TextStrong,
                Font = DbScreenSans(10F, FontStyle.Bold),
                SelectionBackColor = Color.FromArgb(242, 245, 248),
                SelectionForeColor = TextStrong
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextStrong,
                Font = DbScreenSans(10F),
                SelectionBackColor = Color.FromArgb(230, 236, 242),
                SelectionForeColor = TextStrong
            }
        };
        _stopsGrid.Columns.Add(MakeTextColumn("Halt (RL 100 Code)", nameof(StopRecord.Name), 320));
        _stopsGrid.Columns.Add(MakeTextColumn("Ankunft", nameof(StopRecord.Arrival), 90));
        _stopsGrid.Columns.Add(MakeTextColumn("Abfahrt", nameof(StopRecord.Departure), 90));
        _stopsGrid.Columns.Add(MakeTextColumn("Gleis", nameof(StopRecord.Platform), 90));
        right.Controls.Add(_stopsGrid, 0, 5);

        var savePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = AppBackground
        };
        savePanel.Controls.Add(MakeButton("Speichern und an RIS-Communicator senden", SaveData, 250, primary: true));
        savePanel.Controls.Add(MakeButton("Aus Datenbank neu laden", LoadData, 190));
        right.Controls.Add(savePanel, 0, 6);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextMuted,
            BackColor = AppBackground
        };
        _statusLabel.Padding = new Padding(22, 0, 22, 0);
        root.Controls.Add(_statusLabel, 0, 2);
    }

    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "RIS-Logo.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        }

        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }
    }

    private Control CreateStationPicker()
    {
        var panel = CreateSurfacePanel(1, new Padding(20, 12, 20, 12));
        panel.ColumnCount = 5;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        panel.Controls.Add(new Label
        {
            Text = "RIS Haltauswahl",
            Dock = DockStyle.Fill,
            Font = DbScreenSans(10.5F, FontStyle.Bold),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _stationSearchBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 30,
            Margin = new Padding(0, 8, 10, 0),
            PlaceholderText = "Halt oder RIL 100 suchen",
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = FieldBackground,
            ForeColor = TextStrong,
            Font = DbScreenSans(10F)
        };
        _stationSearchBox.KeyDown += async (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                await SearchStations();
            }
        };
        panel.Controls.Add(_stationSearchBox, 1, 0);

        _stationCombo = new ComboBox
        {
            Dock = DockStyle.Top,
            Height = 30,
            Margin = new Padding(0, 8, 10, 0),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DataSource = _stationOptions,
            DisplayMember = nameof(StationOption.DisplayName),
            ValueMember = nameof(StationOption.Name),
            Font = DbScreenSans(10F)
        };
        panel.Controls.Add(_stationCombo, 2, 0);

        panel.Controls.Add(MakeButton("Suchen", async () => await SearchStations(), 96), 3, 0);
        panel.Controls.Add(MakeButton("In Halt übernehmen", ApplySelectedStationToCurrentStop, 172, primary: true), 4, 0);

        return panel;
    }

    private Control CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Surface,
            Padding = new Padding(22, 0, 22, 0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

        header.Controls.Add(CreateDbLogo(), 0, 0);
        header.Controls.Add(new Label
        {
            Text = "RIS Eingabeprogramm für den RIS Communicator",
            Dock = DockStyle.Fill,
            Font = new Font("DB Sans", 18F, FontStyle.Bold),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);
        var accountPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Surface,
            Margin = Padding.Empty
        };
        accountPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        accountPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        accountPanel.Controls.Add(new Label
        {
            Text = "Angemeldeter DB User:",
            Dock = DockStyle.Fill,
            Font = new Font("DB Sans", 10F),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.BottomRight
        }, 0, 0);
        accountPanel.Controls.Add(new Label
            {
                Text = $"{_currentUser.DisplayName} · {_currentUser.RoleName}",
                Dock = DockStyle.Fill,
                Font = new Font("DB Sans", 8.5F),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.TopRight
            }, 0, 1);
        header.Controls.Add(accountPanel, 2, 0);
        header.Controls.Add(new Label
        {
            Text = "⋮",
            Dock = DockStyle.Fill,
            Font = new Font("DB Sans", 22F),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleRight
        }, 3, 0);

        return header;
    }

    private static Control CreateDbLogo()
    {
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "db_logo_small.png");
        if (File.Exists(logoPath))
        {
            return new PictureBox
            {
                Image = Image.FromFile(logoPath),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 22, 18, 22),
                BackColor = Surface
            };
        }

        return new Label
        {
            Text = "DB",
            AutoSize = false,
            Width = 50,
            Height = 34,
            Margin = new Padding(0, 20, 18, 20),
            Font = new Font("Arial", 18F, FontStyle.Bold),
            ForeColor = DbRed,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter
        };
    }

    private static Control CreateNavigationItem(string text, bool selected, Action? click = null)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 14, 0),
            Font = new Font("DB Sans", 10.5F, selected ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = selected ? TextStrong : TextMuted,
            BackColor = selected ? Color.FromArgb(242, 245, 248) : Surface,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = click is null ? Cursors.Default : Cursors.Hand
        };

        if (click is not null)
        {
            label.Click += (_, _) => click();
            label.MouseEnter += (_, _) => label.BackColor = Color.FromArgb(242, 245, 248);
            label.MouseLeave += (_, _) => label.BackColor = Surface;
        }

        return label;
    }

    private static Control CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("DB Sans", 11F, FontStyle.Bold),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static TableLayoutPanel CreateSurfacePanel(int rows, Padding padding)
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            Padding = padding,
            BackColor = Surface
        };
    }

    private TextBox AddField(TableLayoutPanel parent, string labelText, int column, int row)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            Padding = new Padding(0, 0, 10, 8),
            BackColor = Surface
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        parent.Controls.Add(panel, column, row);

        panel.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            Font = DbScreenHead(9.5F, FontStyle.Bold),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);

        var box = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 30,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = FieldBackground,
            ForeColor = TextStrong,
            Font = DbScreenSans(10.5F)
        };
        panel.Controls.Add(box, 0, 1);
        box.TextChanged += (_, _) => UpdateSelectedTrainFromFields();
        return box;
    }

    private static DataGridViewTextBoxColumn MakeTextColumn(string header, string property, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            AutoSizeMode = header == "Halt" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
        };
    }

    private static Font DbScreenSans(float size, FontStyle style = FontStyle.Regular)
    {
        return CreatePreferredFont(DbScreenSansFamilies, size, style);
    }

    private static Font DbScreenHead(float size, FontStyle style = FontStyle.Regular)
    {
        return CreatePreferredFont(DbScreenHeadFamilies, size, style);
    }

    private static Font CreatePreferredFont(IEnumerable<string> families, float size, FontStyle style)
    {
        foreach (var family in families)
        {
            var font = new Font(family, size, style);
            if (font.FontFamily.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
        }

        return new Font("DB Sans", size, style);
    }

    private static Button MakeButton(string text, Action click, int width = 150, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            Margin = new Padding(0, 0, 8, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? DbRed : Surface,
            ForeColor = primary ? Color.White : TextStrong,
            Font = new Font("DB Sans", 9.5F, FontStyle.Bold)
        };
        button.FlatAppearance.BorderColor = primary ? DbRed : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(190, 0, 22) : Color.FromArgb(246, 248, 250);
        button.Click += (_, _) => click();
        return button;
    }

    private async Task SearchStations()
    {
        var query = _stationSearchBox.Text.Trim();
        if (query.Length < 2)
        {
            SetStatus("Bitte mindestens 2 Zeichen für die Haltsuche eingeben.");
            return;
        }

        try
        {
            SetStatus("RIS::Stations API wird nun abgefragt...");
            using var response = await _httpClient.GetAsync($"/api/station-search/{Uri.EscapeDataString(query)}");
            if (!response.IsSuccessStatusCode)
            {
                SetStatus($"RIS::Stations API ist nicht erreichbar: {(int)response.StatusCode} {response.ReasonPhrase}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var stations = JsonSerializer.Deserialize<List<StationOption>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            _stationOptions.Clear();
            foreach (var station in stations.Where(item => !string.IsNullOrWhiteSpace(item.Name)).Take(25))
            {
                _stationOptions.Add(station);
            }

            if (_stationOptions.Count > 0)
            {
                _stationCombo.SelectedIndex = 0;
                _stationCombo.DroppedDown = true;
                SetStatus($"RIS::Stations API: {_stationOptions.Count} Treffer für \"{query}\".");
            }
            else
            {
                SetStatus($"RIS::Stations API: keine Treffer für \"{query}\".");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"RIS::Stations API konnte nicht geladen werden: {ex.Message}");
        }
    }

    private void ApplySelectedStationToCurrentStop()
    {
        if (_stationCombo.SelectedItem is not StationOption station || string.IsNullOrWhiteSpace(station.Name))
        {
            SetStatus("Bitte zuerst einen RIS-Halt auswählen.");
            return;
        }

        if (_stopsGrid.CurrentRow?.DataBoundItem is StopRecord stop)
        {
            stop.Name = station.Name;
            _stops.ResetBindings();
            UpdateSelectedTrainFromFields();
            SetStatus($"Halt wurde übernommen: {station.DisplayName}");
            return;
        }

        _stops.Add(new StopRecord { Name = station.Name });
        UpdateSelectedTrainFromFields();
        SetStatus($"Neuer Halt aus RIS::Stations API wurde angelegt: {station.DisplayName}");
    }

    private void ImportJsonIfDatabaseIsEmpty()
    {
        if (_database.HasTrains() || !File.Exists(_dataFilePath))
        {
            return;
        }

        var imported = ReadJsonTrainFile(_dataFilePath);
        if (imported.Count > 0)
        {
            _database.SaveTrains(imported);
        }
    }

    private void LoadData()
    {
        CommitCurrentTrain();
        _trains.Clear();
        _stops.Clear();

        var records = _database.LoadTrains();
        if (records.Count == 0)
        {
            records.Add(TrainRecord.CreateDemo());
            _database.SaveTrains(records);
        }

        foreach (var record in records)
        {
            _trains.Add(record);
        }

        RefreshTrainList();
        SelectFirstTrain();
        SetStatus($"Datenbank: {_databaseFilePath} | App-Export: {_dataFilePath}");
    }

    private async void SaveData()
    {
        CommitCurrentTrain();

        var invalid = _trains.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Number) || t.Stops.Count < 2);
        if (invalid is not null)
        {
            MessageBox.Show(this, "Jeder Zug braucht eine Zugnummer und mindestens zwei Halte.", "Prüfung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var records = _trains.Select(t =>
        {
            t.Normalize();
            return t;
        }).ToList();

        _database.SaveTrains(records);
        ExportJson(records);
        SetStatus("Gespeichert. Cloudflare-Datenbereitstellung wird gestartet...");

        var publishStatus = await Task.Run(PublishDataToCloudflare);
        SetStatus($"Gespeichert in Datenbank und an die App exportiert. {publishStatus}");
    }

    private void ImportJsonFromDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "RIS JSON-Daten importieren",
            Filter = "RIS Zugdaten (trains.json)|trains.json|JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*",
            FileName = Path.GetFileName(_dataFilePath),
            InitialDirectory = Path.GetDirectoryName(_dataFilePath)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var imported = ReadJsonTrainFile(dialog.FileName);
        if (imported.Count == 0)
        {
            MessageBox.Show(this, "In der JSON-Datei wurden keine gültigen Züge gefunden.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this, "Aktuelle Datenbank mit dieser JSON-Datei ersetzen?", "JSON importieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _database.SaveTrains(imported);
        ExportJson(imported);
        LoadData();
        SetStatus($"Importiert: {dialog.FileName}");
    }

    private void OpenCredentials()
    {
        using var form = new CredentialsForm(_database);
        form.ShowDialog(this);
    }

    private List<TrainRecord> ReadJsonTrainFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var records = JsonSerializer.Deserialize<List<TrainRecord>>(json, _jsonOptions) ?? [];
            return records
                .Where(t => !string.IsNullOrWhiteSpace(t.Number))
                .Select(t =>
                {
                    t.Id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString("N") : t.Id;
                    t.Normalize();
                    return t;
                })
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "JSON konnte nicht geladen werden", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return [];
        }
    }

    private void ExportJson(List<TrainRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath)!);
        var json = JsonSerializer.Serialize(records, _jsonOptions);
        File.WriteAllText(_dataFilePath, json);
        WriteDataVersion();
    }

    private void WriteDataVersion()
    {
        var dataDirectory = Path.GetDirectoryName(_dataFilePath)!;
        var versionPath = Path.Combine(dataDirectory, "data-version.json");
        var now = DateTimeOffset.Now;
        var payload = new
        {
            version = now.ToString("yyyyMMddHHmmss"),
            builtAt = now.ToString("O")
        };

        File.WriteAllText(versionPath, JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private string PublishDataToCloudflare()
    {
        try
        {
            var root = LocateWorkspaceRoot();
            if (root is null)
            {
                return "Cloudflare nicht aktualisiert: Projektordner wurde nicht gefunden.";
            }

            var configPath = Path.Combine(root, "cloudflare.publish.local.json");
            if (!File.Exists(configPath))
            {
                return "Cloudflare nicht aktualisiert: cloudflare.publish.local.json fehlt.";
            }

            var scriptPath = Path.Combine(root, "scripts", "publish-data-to-cloudflare.ps1");
            if (!File.Exists(scriptPath))
            {
                return "Cloudflare nicht aktualisiert: publish-data-to-cloudflare.ps1 fehlt.";
            }

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var detail = string.Join(Environment.NewLine, new[] { output, error }
                    .Where(text => !string.IsNullOrWhiteSpace(text)))
                    .Trim();

                if (detail.Length > 1800)
                {
                    detail = detail[^1800..];
                }

                return $"Cloudflare-Upload fehlgeschlagen: {detail}";
            }

            return "Cloudflare-Daten sind aktualisiert.";
        }
        catch (Exception ex)
        {
            return $"Cloudflare-Upload fehlgeschlagen: {ex.Message}";
        }
    }

    private string? LocateWorkspaceRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(_dataFilePath)!);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "scripts"))
                && File.Exists(Path.Combine(directory.FullName, "app.js")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private void LoadSelectedTrain()
    {
        if (_loadingSelection)
        {
            return;
        }

        if (_trainList.SelectedItem is not TrainRecord selectedTrain)
        {
            return;
        }

        CommitCurrentTrain(refreshList: false);

        _loadingSelection = true;
        _numberBox.Text = selectedTrain.Number;
        _lineBox.Text = selectedTrain.Line;
        _fromBox.Text = selectedTrain.From;
        _toBox.Text = selectedTrain.To;
        _durationBox.Text = selectedTrain.Duration;
        _operatorBox.Text = selectedTrain.Operator;
        _stops.Clear();
        foreach (var stop in selectedTrain.Stops)
        {
            _stops.Add(stop.Clone());
        }
        _loadedTrain = selectedTrain;
        _loadingSelection = false;
    }

    private void UpdateSelectedTrainFromFields()
    {
        if (_loadingSelection || _loadedTrain is not TrainRecord train)
        {
            return;
        }

        train.Number = _numberBox.Text.Trim();
        train.Line = _lineBox.Text.Trim();
        train.From = _fromBox.Text.Trim();
        train.To = _toBox.Text.Trim();
        train.Duration = _durationBox.Text.Trim();
        train.Operator = _operatorBox.Text.Trim();
        RefreshTrainList();
    }

    private void CommitCurrentTrain(bool refreshList = true)
    {
        if (_loadingSelection || _loadedTrain is not TrainRecord train)
        {
            return;
        }

        train.Number = _numberBox.Text.Trim();
        train.Line = _lineBox.Text.Trim();
        train.From = _fromBox.Text.Trim();
        train.To = _toBox.Text.Trim();
        train.Duration = _durationBox.Text.Trim();
        train.Operator = _operatorBox.Text.Trim();
        train.Stops = _stops
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s.Clone())
            .ToList();
        train.Normalize();
        if (refreshList)
        {
            RefreshTrainList();
        }
    }

    private void AddTrain()
    {
        CommitCurrentTrain();
        var train = new TrainRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Number = "0000",
            Line = "RE",
            From = "Start",
            To = "Ziel",
            Stops =
            [
                new StopRecord { Name = "Start", Departure = "08:00" },
                new StopRecord { Name = "Ziel", Arrival = "09:00" }
            ]
        };
        train.Duration = train.EstimateDuration();
        _trains.Add(train);
        _loadedTrain = null;
        RefreshTrainList();
        _trainList.SelectedItem = train;
    }

    private void RefreshTrainList()
    {
        var selected = _loadedTrain;
        RefreshOperatorFilterItems();
        var filtered = GetFilteredTrains();

        _loadingSelection = true;
        _trainList.DataSource = null;
        _trainList.DataSource = filtered;
        _trainList.DisplayMember = nameof(TrainRecord.DisplayName);
        _loadingSelection = false;
        if (selected is not null && filtered.Contains(selected))
        {
            _trainList.SelectedItem = selected;
        }
    }

    private void RefreshOperatorFilterItems()
    {
        if (_operatorFilterBox is null)
        {
            return;
        }

        var selected = _operatorFilterBox.SelectedItem as string;
        var options = new List<string> { "Alle Verkehrsunternehmen" };
        options.AddRange(_trains
            .Select(t => t.Operator.Trim())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.CurrentCultureIgnoreCase));

        _operatorFilterBox.SelectedIndexChanged -= OperatorFilterChanged;
        _operatorFilterBox.Items.Clear();
        foreach (var option in options)
        {
            _operatorFilterBox.Items.Add(option);
        }

        _operatorFilterBox.SelectedItem = !string.IsNullOrWhiteSpace(selected) && options.Contains(selected)
            ? selected
            : options[0];
        _operatorFilterBox.SelectedIndexChanged += OperatorFilterChanged;
    }

    private void OperatorFilterChanged(object? sender, EventArgs e)
    {
        ApplyTrainFilter();
    }

    private List<TrainRecord> GetFilteredTrains()
    {
        var query = _trains.AsEnumerable();
        var operatorFilter = _operatorFilterBox?.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(operatorFilter) && operatorFilter != "Alle Verkehrsunternehmen")
        {
            query = query.Where(t => string.Equals(t.Operator.Trim(), operatorFilter, StringComparison.OrdinalIgnoreCase));
        }

        var search = _trainFilterBox?.Text.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(t =>
                t.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || t.Operator.Contains(search, StringComparison.OrdinalIgnoreCase)
                || t.Stops.Any(s => s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        return query.ToList();
    }

    private void ApplyTrainFilter()
    {
        if (_trainList is null)
        {
            return;
        }

        var selected = _loadedTrain;
        var filtered = GetFilteredTrains();
        _loadingSelection = true;
        _trainList.DataSource = null;
        _trainList.DataSource = filtered;
        _trainList.DisplayMember = nameof(TrainRecord.DisplayName);
        _loadingSelection = false;

        if (selected is not null && filtered.Contains(selected))
        {
            _trainList.SelectedItem = selected;
        }
        else if (filtered.Count > 0)
        {
            _trainList.SelectedIndex = 0;
        }
        else
        {
            ClearEditorFields();
        }
    }

    private void DeleteTrain()
    {
        if (_trainList.SelectedItem is not TrainRecord train)
        {
            return;
        }

        if (MessageBox.Show(this, $"Zug {train.DisplayName} löschen?", "Zug löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var index = _trainList.SelectedIndex;
        _trains.Remove(train);
        if (ReferenceEquals(_loadedTrain, train))
        {
            _loadedTrain = null;
        }
        if (_trains.Count == 0)
        {
            _trains.Add(TrainRecord.CreateDemo());
        }

        RefreshTrainList();
        if (_trainList.Items.Count > 0)
        {
            _trainList.SelectedIndex = Math.Min(index, _trainList.Items.Count - 1);
        }
        else
        {
            ClearEditorFields();
        }
    }

    private void CopySelectedTrain()
    {
        CommitCurrentTrain();
        if (_trainList.SelectedItem is not TrainRecord source)
        {
            return;
        }

        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Number = NextCopyNumber(source.Number);
        copy.Normalize();

        var sourceIndex = _trains.IndexOf(source);
        if (sourceIndex >= 0 && sourceIndex < _trains.Count - 1)
        {
            _trains.Insert(sourceIndex + 1, copy);
        }
        else
        {
            _trains.Add(copy);
        }

        _loadedTrain = copy;
        RefreshTrainList();
        _trainList.SelectedItem = copy;
        LoadSelectedTrain();
        SetStatus($"Zug kopiert: {copy.DisplayName}");
    }

    private string NextCopyNumber(string number)
    {
        if (!int.TryParse(number, out var numeric))
        {
            return number;
        }

        var used = _trains.Select(t => t.Number).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = numeric;
        do
        {
            candidate++;
        }
        while (used.Contains(candidate.ToString()));

        return candidate.ToString();
    }

    private void SelectFirstTrain()
    {
        if (_trainList.Items.Count == 0 || _trains.Count == 0)
        {
            ClearEditorFields();
            return;
        }

        _trainList.SelectedIndex = 0;
    }

    private void ClearEditorFields()
    {
        _loadingSelection = true;
        _numberBox.Clear();
        _lineBox.Clear();
        _fromBox.Clear();
        _toBox.Clear();
        _durationBox.Clear();
        _operatorBox.Clear();
        _stops.Clear();
        _loadedTrain = null;
        _loadingSelection = false;
    }

    private void AddStop()
    {
        _stops.Add(new StopRecord { Name = "Neuer Halt" });
    }

    private void DeleteStop()
    {
        if (_stopsGrid.CurrentRow?.DataBoundItem is not StopRecord stop)
        {
            return;
        }

        _stops.Remove(stop);
    }

    private void CalculateDuration()
    {
        CommitCurrentTrain();
        if (_trainList.SelectedItem is not TrainRecord train)
        {
            return;
        }

        train.Duration = train.EstimateDuration();
        _durationBox.Text = train.Duration;
        SetStatus("Dauer aus erstem Abfahrts- und letztem Ankunftshalt berechnet.");
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }
}

public sealed class TrainRecord
{
    [JsonIgnore]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("line")]
    public string Line { get; set; } = "";

    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("to")]
    public string To { get; set; } = "";

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = "";

    [JsonIgnore]
    public string Operator { get; set; } = "";

    [JsonPropertyName("stops")]
    public List<StopRecord> Stops { get; set; } = [];

    [JsonIgnore]
    public string DisplayName => $"{Line} ({Number}) {From} - {To}";

    public void Normalize()
    {
        Number = (Number ?? "").Trim();
        Line = string.IsNullOrWhiteSpace(Line) ? "RE" : Line.Trim();
        Operator = (Operator ?? "").Trim();
        Stops = Stops.Where(s => !string.IsNullOrWhiteSpace(s.Name)).Select(s => s.Clone()).ToList();
        From = string.IsNullOrWhiteSpace(From) ? Stops.FirstOrDefault()?.Name ?? "" : From.Trim();
        To = string.IsNullOrWhiteSpace(To) ? Stops.LastOrDefault()?.Name ?? "" : To.Trim();
        Duration = string.IsNullOrWhiteSpace(Duration) ? EstimateDuration() : Duration.Trim();
    }

    public TrainRecord Clone()
    {
        return new TrainRecord
        {
            Id = Id,
            Number = Number ?? "",
            Line = Line ?? "",
            From = From ?? "",
            To = To ?? "",
            Duration = Duration ?? "",
            Operator = Operator ?? "",
            Stops = Stops.Select(stop => stop.Clone()).ToList()
        };
    }

    public string EstimateDuration()
    {
        var start = Stops.FirstOrDefault()?.Departure;
        var end = Stops.LastOrDefault()?.Arrival;
        if (!TimeOnly.TryParse(start, out var startTime) || !TimeOnly.TryParse(end, out var endTime))
        {
            return Duration;
        }

        var minutes = (int)(endTime - startTime).TotalMinutes;
        if (minutes < 0)
        {
            minutes += 24 * 60;
        }

        return $"{minutes / 60} h {minutes % 60} min";
    }

    public static TrainRecord CreateDemo()
    {
        var train = new TrainRecord
        {
            Number = "4865",
            Line = "RE2",
            From = "Hof Hbf",
            To = "München Hbf",
            Stops =
            [
                new StopRecord { Name = "Hof Hbf", Departure = "14:34", Platform = "6a" },
                new StopRecord { Name = "Marktredwitz", Arrival = "15:01", Departure = "15:01", Platform = "5" },
                new StopRecord { Name = "Weiden(Oberpf)", Arrival = "15:41", Departure = "15:44", Platform = "2" },
                new StopRecord { Name = "Schwandorf", Arrival = "16:09", Departure = "16:10", Platform = "4" },
                new StopRecord { Name = "Regensburg Hbf", Arrival = "16:38", Departure = "16:46", Platform = "1" },
                new StopRecord { Name = "Eggmühl", Arrival = "17:00", Departure = "17:01", Platform = "2" },
                new StopRecord { Name = "Neufahrn(Niederbay)", Arrival = "17:11", Departure = "17:12", Platform = "1" },
                new StopRecord { Name = "Ergoldsbach", Arrival = "17:15", Departure = "17:16", Platform = "2" },
                new StopRecord { Name = "Landshut(Bay)Hbf", Arrival = "17:28", Departure = "17:30", Platform = "6" },
                new StopRecord { Name = "Moosburg", Arrival = "17:40", Departure = "17:41", Platform = "2" },
                new StopRecord { Name = "Freising", Arrival = "17:50", Departure = "17:51", Platform = "2" },
                new StopRecord { Name = "München Hbf", Arrival = "18:16", Platform = "31" }
            ]
        };
        train.Duration = train.EstimateDuration();
        return train;
    }
}

[JsonConverter(typeof(StopRecordJsonConverter))]
public sealed class StopRecord
{
    public string Name { get; set; } = "";
    public string Arrival { get; set; } = "";
    public string Departure { get; set; } = "";
    public string Platform { get; set; } = "";

    public StopRecord Clone()
    {
        return new StopRecord
        {
            Name = (Name ?? "").Trim(),
            Arrival = (Arrival ?? "").Trim(),
            Departure = (Departure ?? "").Trim(),
            Platform = (Platform ?? "").Trim()
        };
    }
}

public sealed class StationOption
{
    public string Name { get; set; } = "";
    public string Ril100 { get; set; } = "";
    public string EvaNumber { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    }
}

public sealed class StopRecordJsonConverter : JsonConverter<StopRecord>
{
    public override StopRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Ein Halt muss ein Array sein.");
        }

        var cells = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            cells.Add(reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "");
        }

        return new StopRecord
        {
            Name = cells.ElementAtOrDefault(0) ?? "",
            Arrival = cells.ElementAtOrDefault(1) ?? "",
            Departure = cells.ElementAtOrDefault(2) ?? "",
            Platform = cells.ElementAtOrDefault(3) ?? ""
        };
    }

    public override void Write(Utf8JsonWriter writer, StopRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(value.Name);
        writer.WriteStringValue(value.Arrival);
        writer.WriteStringValue(value.Departure);
        writer.WriteStringValue(value.Platform);
        writer.WriteEndArray();
    }
}
