namespace RisDataEditor;

public sealed class LoginForm : Form
{
    private readonly RisDatabase _database;
    private readonly Image? _background;
    private readonly TextBox _usernameBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly CheckBox _rememberBox = new();
    private readonly Label _messageLabel = new();

    public LoginUser? LoggedInUser { get; private set; }

    public LoginForm(RisDatabase database)
    {
        _database = database;
        _background = LoadImage("login_background.jpg");
        BuildUi();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        if (_background is null)
        {
            using var fallback = new SolidBrush(Color.FromArgb(210, 220, 226));
            e.Graphics.FillRectangle(fallback, ClientRectangle);
            return;
        }

        var target = GetCoverRectangle(_background.Size, ClientSize);
        e.Graphics.DrawImage(_background, target);
    }

    private void BuildUi()
    {
        Text = "RIS-Communicator Backoffice";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1200, 720);
        Font = new Font("DBScreenHead", 10F);
        Icon = TryLoadIcon();

        var topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.FromArgb(238, 238, 238)
        };
        Controls.Add(topBar);

        var dbLogo = new PictureBox
        {
            Image = LoadImage("db_logo_small.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(18, 10),
            Size = new Size(52, 34),
            BackColor = topBar.BackColor
        };
        topBar.Controls.Add(dbLogo);

        topBar.Controls.Add(new Label
        {
            Text = "RIS-Communicator Backoffice",
            Location = new Point(96, 13),
            AutoSize = true,
            Font = new Font("DBScreenHead", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 39, 47)
        });

        var contact = new Button
        {
            Text = "ⓘ Kontakt",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Width - 128, 8),
            Size = new Size(108, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(238, 238, 238),
            ForeColor = Color.FromArgb(35, 39, 47)
        };
        contact.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
        contact.Click += (_, _) => MessageBox.Show(this, "Zur Kontaktmöglichkeit wenden Sie sich bitte an den RIS-Fachbetrieb unter folgender E-Mail Adresse: RIS-Fachbetrieb@deutschebahn.com ", "Kontakt", MessageBoxButtons.OK, MessageBoxIcon.Information);
        topBar.Controls.Add(contact);
        topBar.Resize += (_, _) => contact.Left = topBar.Width - contact.Width - 18;

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            BackColor = Color.FromArgb(238, 238, 238)
        };
        footer.Controls.Add(new LinkLabel
        {
            Text = "Datenschutzhinweise | Impressum",
            AutoSize = true,
            Location = new Point(18, 4),
            LinkColor = Color.FromArgb(50, 110, 180)
        });
        Controls.Add(footer);

        var loginPanel = new Panel
        {
            Size = new Size(810, 490),
            BackColor = Color.FromArgb(190, 0, 0, 0)
        };
        Controls.Add(loginPanel);

        var risIcon = new PictureBox
        {
            Image = LoadImage("icon.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(72, 46),
            Size = new Size(220, 130),
            BackColor = Color.Transparent
        };
        loginPanel.Controls.Add(risIcon);

        loginPanel.Controls.Add(new Label
        {
            Text = "Backoffice",
            Location = new Point(295, 74),
            AutoSize = true,
            Font = new Font("DB Sans", 48F, FontStyle.Bold),
            ForeColor = Color.FromArgb(224, 224, 224),
            BackColor = Color.Transparent
        });

        loginPanel.Controls.Add(new Label
        {
            Text = "                                              Bitte im System Anmelden!",
            Location = new Point(54, 206),
            AutoSize = true,
            Font = new Font("DBScreenSans", 14F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        });

        AddLoginLabel(loginPanel, "Benutzername:", 98, 270);
        AddLoginLabel(loginPanel, "Passwort:", 98, 324);

        ConfigureTextBox(_usernameBox, "Benutzername eingeben", 238, 263);
        ConfigureTextBox(_passwordBox, "Passwort eingeben", 238, 317);
        _passwordBox.UseSystemPasswordChar = true;
        loginPanel.Controls.Add(_usernameBox);
        loginPanel.Controls.Add(_passwordBox);

        _rememberBox.Text = "Angemeldet bleiben";
        _rememberBox.Location = new Point(238, 374);
        _rememberBox.AutoSize = true;
        _rememberBox.ForeColor = Color.White;
        _rememberBox.BackColor = Color.Transparent;
        loginPanel.Controls.Add(_rememberBox);

        var loginButton = MakeLoginButton("Anmelden", 238, 414, 100);
        loginButton.Click += (_, _) => TryLogin();
        loginPanel.Controls.Add(loginButton);

        var forgotButton = MakeLoginButton("Passwort vergessen", 350, 414, 172);
        forgotButton.Click += (_, _) => MessageBox.Show(this, "Zur Passwortrücksetzung wenden Sie sich bitte an den RIS-Fachbetrieb unter: RIS-Fachbetrieb@deutschebahn.com", "Passwort vergessen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        loginPanel.Controls.Add(forgotButton);

        _messageLabel.Location = new Point(238, 454);
        _messageLabel.Size = new Size(470, 24);
        _messageLabel.ForeColor = Color.FromArgb(255, 190, 190);
        _messageLabel.BackColor = Color.Transparent;
        loginPanel.Controls.Add(_messageLabel);

        AcceptButton = loginButton;
        Resize += (_, _) => CenterLoginPanel(loginPanel);
        Shown += (_, _) =>
        {
            CenterLoginPanel(loginPanel);
            _usernameBox.Focus();
        };
    }

    private static void AddLoginLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(120, 24),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleRight
        });
    }

    private static void ConfigureTextBox(TextBox box, string placeholder, int x, int y)
    {
        box.Location = new Point(x, y);
        box.Size = new Size(458, 28);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.PlaceholderText = placeholder;
        box.BackColor = Color.White;
        box.ForeColor = Color.FromArgb(45, 45, 45);
    }

    private static Button MakeLoginButton(string text, int x, int y, int width)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(45, 45, 45)
        };
        button.FlatAppearance.BorderColor = Color.White;
        return button;
    }

    private void TryLogin()
    {
        var user = _database.ValidateLogin(_usernameBox.Text, _passwordBox.Text);
        if (user is null)
        {
            _messageLabel.Text = "Der eingegebene Benutzername oder das Passwort ist falsch.";
            return;
        }

        LoggedInUser = user;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CenterLoginPanel(Control panel)
    {
        panel.Left = Math.Max(20, (ClientSize.Width - panel.Width) / 2);
        panel.Top = Math.Max(82, (ClientSize.Height - panel.Height) / 2 + 28);
    }

    private static Rectangle GetCoverRectangle(Size imageSize, Size canvasSize)
    {
        var scale = Math.Max(canvasSize.Width / (float)imageSize.Width, canvasSize.Height / (float)imageSize.Height);
        var width = (int)(imageSize.Width * scale);
        var height = (int)(imageSize.Height * scale);
        return new Rectangle((canvasSize.Width - width) / 2, (canvasSize.Height - height) / 2, width, height);
    }

    private static Image? LoadImage(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        return File.Exists(path) ? Image.FromFile(path) : null;
    }

    private static Icon? TryLoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RIS-Logo.ico");
        return File.Exists(path) ? new Icon(path) : null;
    }
}
