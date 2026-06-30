using System.ComponentModel;

namespace RisDataEditor;

public sealed class CredentialsForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(238, 243, 246);
    private static readonly Color Surface = Color.White;
    private static readonly Color TextStrong = Color.FromArgb(31, 35, 45);
    private static readonly Color TextMuted = Color.FromArgb(99, 104, 114);
    private static readonly Color DbRed = Color.FromArgb(224, 0, 26);
    private static readonly Color Border = Color.FromArgb(218, 222, 226);

    private readonly RisDatabase _database;
    private readonly BindingList<RoleRecord> _roles = [];
    private readonly BindingList<UserRecord> _users = [];

    private ListBox _roleList = null!;
    private TextBox _roleNameBox = null!;
    private TextBox _roleDescriptionBox = null!;
    private DataGridView _usersGrid = null!;
    private TextBox _usernameBox = null!;
    private TextBox _displayNameBox = null!;
    private TextBox _passwordBox = null!;
    private ComboBox _roleCombo = null!;
    private CheckBox _activeBox = null!;
    private int _editingRoleId;
    private int _editingUserId;

    public CredentialsForm(RisDatabase database)
    {
        _database = database;
        BuildUi();
        LoadData();
    }

    private void BuildUi()
    {
        Text = "Rollen und Anmeldedaten";
        Size = new Size(980, 680);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppBackground;
        Font = new Font("DB Sans", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18),
            BackColor = AppBackground
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var rolesPanel = CreatePanel(4);
        rolesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        rolesPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rolesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        rolesPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(rolesPanel, 0, 0);

        rolesPanel.Controls.Add(Header("Rollen"), 0, 0);
        _roleList = new ListBox
        {
            Dock = DockStyle.Fill,
            DataSource = _roles,
            DisplayMember = nameof(RoleRecord.Name),
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(248, 250, 252)
        };
        _roleList.SelectedIndexChanged += (_, _) => LoadSelectedRole();
        rolesPanel.Controls.Add(_roleList, 0, 1);

        var roleFields = CreatePanel(2);
        roleFields.Padding = new Padding(0, 10, 0, 0);
        roleFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        roleFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        _roleNameBox = AddField(roleFields, "Rollenname", 0);
        _roleDescriptionBox = AddField(roleFields, "Beschreibung", 1);
        rolesPanel.Controls.Add(roleFields, 0, 2);

        var roleButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Surface,
            Padding = new Padding(0, 8, 0, 0)
        };
        roleButtons.Controls.Add(Button("Rolle speichern", SaveRole, 136, true));
        roleButtons.Controls.Add(Button("Neue Rolle", NewRole, 108));
        rolesPanel.Controls.Add(roleButtons, 0, 3);

        var userPanel = CreatePanel(4);
        userPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        userPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        userPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        userPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        userPanel.Padding = new Padding(18, 0, 0, 0);
        root.Controls.Add(userPanel, 1, 0);

        userPanel.Controls.Add(Header("Anmeldedaten"), 0, 0);
        _usersGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _users,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Surface,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            EnableHeadersVisualStyles = false
        };
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Benutzer", DataPropertyName = nameof(UserRecord.Username), Width = 130 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", DataPropertyName = nameof(UserRecord.DisplayName), Width = 170 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rolle", DataPropertyName = nameof(UserRecord.RoleName), Width = 150 });
        _usersGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Aktiv", DataPropertyName = nameof(UserRecord.IsActive), Width = 60 });
        _usersGrid.SelectionChanged += (_, _) => LoadSelectedUser();
        userPanel.Controls.Add(_usersGrid, 0, 1);

        var userFields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Surface,
            Padding = new Padding(0, 10, 0, 0)
        };
        userFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        userFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        userFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        userFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        userFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _usernameBox = AddField(userFields, "Benutzername", 0, 0);
        _displayNameBox = AddField(userFields, "Anzeigename", 1, 0);
        _passwordBox = AddField(userFields, "Neues Passwort", 0, 1);
        _passwordBox.UseSystemPasswordChar = true;
        _roleCombo = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = 30,
            DataSource = _roles,
            DisplayMember = nameof(RoleRecord.Name)
        };
        var roleComboPanel = FieldShell("Rolle");
        roleComboPanel.Controls.Add(_roleCombo, 0, 1);
        userFields.Controls.Add(roleComboPanel, 1, 1);
        _activeBox = new CheckBox
        {
            Text = "Benutzer aktiv",
            Dock = DockStyle.Fill,
            Checked = true,
            ForeColor = TextStrong
        };
        userFields.Controls.Add(_activeBox, 0, 2);
        userPanel.Controls.Add(userFields, 0, 2);

        var userButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Surface,
            Padding = new Padding(0, 10, 0, 0)
        };
        userButtons.Controls.Add(Button("Benutzer speichern", SaveUser, 156, true));
        userButtons.Controls.Add(Button("Neuer Benutzer", NewUser, 128));
        userPanel.Controls.Add(userButtons, 0, 3);
    }

    private void LoadData()
    {
        _roles.Clear();
        foreach (var role in _database.LoadRoles())
        {
            _roles.Add(role);
        }

        _users.Clear();
        foreach (var user in _database.LoadUsers())
        {
            _users.Add(user);
        }

        SelectFirstRoleIfAvailable();
    }

    private void SelectFirstRoleIfAvailable()
    {
        if (_roleList.Items.Count > 0)
        {
            _roleList.SelectedIndex = 0;
        }
        else
        {
            _editingRoleId = 0;
            _roleNameBox.Clear();
            _roleDescriptionBox.Clear();
        }

        if (_roleCombo.Items.Count > 0)
        {
            _roleCombo.SelectedIndex = 0;
        }
    }

    private void LoadSelectedRole()
    {
        if (_roleList.SelectedItem is not RoleRecord role)
        {
            return;
        }

        _roleNameBox.Text = role.Name;
        _roleDescriptionBox.Text = role.Description;
        _editingRoleId = role.Id;
    }

    private void LoadSelectedUser()
    {
        if (_usersGrid.CurrentRow?.DataBoundItem is not UserRecord user)
        {
            return;
        }

        _usernameBox.Text = user.Username;
        _displayNameBox.Text = user.DisplayName;
        _passwordBox.Clear();
        _activeBox.Checked = user.IsActive;
        _editingUserId = user.Id;
        var role = _roles.FirstOrDefault(r => r.Id == user.RoleId);
        if (role is not null)
        {
            _roleCombo.SelectedItem = role;
        }
    }

    private void NewRole()
    {
        _roleList.ClearSelected();
        _editingRoleId = 0;
        _roleNameBox.Clear();
        _roleDescriptionBox.Clear();
        _roleNameBox.Focus();
    }

    private void SaveRole()
    {
        if (string.IsNullOrWhiteSpace(_roleNameBox.Text))
        {
            MessageBox.Show(this, "Bitte einen Rollennamen eingeben.", "Rolle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var role = new RoleRecord { Id = _editingRoleId };
        role.Name = _roleNameBox.Text.Trim();
        role.Description = _roleDescriptionBox.Text.Trim();
        _database.SaveRole(role);
        LoadData();
    }

    private void NewUser()
    {
        _usersGrid.ClearSelection();
        _editingUserId = 0;
        _usernameBox.Clear();
        _displayNameBox.Clear();
        _passwordBox.Clear();
        _activeBox.Checked = true;
        if (_roles.Count > 0)
        {
            _roleCombo.SelectedIndex = 0;
        }
        _usernameBox.Focus();
    }

    private void SaveUser()
    {
        if (string.IsNullOrWhiteSpace(_usernameBox.Text))
        {
            MessageBox.Show(this, "Bitte einen Benutzernamen eingeben.", "Benutzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_roleCombo.SelectedItem is not RoleRecord role)
        {
            MessageBox.Show(this, "Bitte eine Rolle auswählen.", "Benutzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var user = new UserRecord { Id = _editingUserId };
        if (user.Id == 0 && string.IsNullOrWhiteSpace(_passwordBox.Text))
        {
            MessageBox.Show(this, "Neue Benutzer brauchen ein Passwort.", "Benutzer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        user.Username = _usernameBox.Text.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(_displayNameBox.Text) ? user.Username : _displayNameBox.Text.Trim();
        user.RoleId = role.Id;
        user.IsActive = _activeBox.Checked;
        _database.SaveUser(user, string.IsNullOrWhiteSpace(_passwordBox.Text) ? null : _passwordBox.Text);
        LoadData();
    }

    private static TableLayoutPanel CreatePanel(int rows)
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = rows,
            ColumnCount = 1,
            BackColor = Surface,
            Padding = new Padding(14)
        };
    }

    private static Label Header(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("DB Sans", 15F, FontStyle.Bold),
            ForeColor = TextStrong,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static TextBox AddField(TableLayoutPanel parent, string label, int row)
    {
        var shell = FieldShell(label);
        var box = CreateTextBox();
        shell.Controls.Add(box, 0, 1);
        parent.Controls.Add(shell, 0, row);
        return box;
    }

    private static TextBox AddField(TableLayoutPanel parent, string label, int column, int row)
    {
        var shell = FieldShell(label);
        var box = CreateTextBox();
        shell.Controls.Add(box, 0, 1);
        parent.Controls.Add(shell, column, row);
        return box;
    }

    private static TableLayoutPanel FieldShell(string label)
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            Padding = new Padding(0, 0, 10, 6),
            BackColor = Surface
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        shell.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        return shell;
    }

    private static TextBox CreateTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Top,
            Height = 28,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static Button Button(string text, Action click, int width, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            Margin = new Padding(0, 0, 8, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? DbRed : Surface,
            ForeColor = primary ? Color.White : TextStrong
        };
        button.FlatAppearance.BorderColor = primary ? DbRed : Border;
        button.Click += (_, _) => click();
        return button;
    }
}
