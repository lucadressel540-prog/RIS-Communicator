namespace RisDataEditor;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        var dataDirectory = AppPaths.LocateDataDirectory();
        var database = new RisDatabase(Path.Combine(dataDirectory, "ris-editor.db"));
        database.Initialize();

        using var login = new LoginForm(database);
        if (login.ShowDialog() == DialogResult.OK)
        {
            Application.Run(new Form1(login.LoggedInUser));
        }
    }    
}
