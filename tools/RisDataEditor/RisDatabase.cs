using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace RisDataEditor;

public sealed class RisDatabase(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath
    }.ToString();

    public string DatabasePath { get; } = databasePath;

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS trains (
                id TEXT PRIMARY KEY,
                number TEXT NOT NULL,
                line TEXT NOT NULL,
                origin TEXT NOT NULL,
                destination TEXT NOT NULL,
                duration TEXT NOT NULL,
                operator TEXT NOT NULL DEFAULT '',
                sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS stops (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                train_id TEXT NOT NULL,
                stop_order INTEGER NOT NULL,
                name TEXT NOT NULL,
                arrival TEXT NOT NULL,
                departure TEXT NOT NULL,
                platform TEXT NOT NULL,
                FOREIGN KEY(train_id) REFERENCES trains(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_stops_train_order ON stops(train_id, stop_order);

            CREATE TABLE IF NOT EXISTS roles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                description TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                role_id INTEGER NOT NULL,
                display_name TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY(role_id) REFERENCES roles(id)
            );
            """;
        command.ExecuteNonQuery();
        EnsureTrainOperatorColumn(connection);
        EnsureDefaultCredentials(connection);
    }

    private static void EnsureTrainOperatorColumn(SqliteConnection connection)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(trains)";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "operator", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var migrate = connection.CreateCommand();
        migrate.CommandText = "ALTER TABLE trains ADD COLUMN operator TEXT NOT NULL DEFAULT ''";
        migrate.ExecuteNonQuery();
    }

    public List<TrainRecord> LoadTrains()
    {
        using var connection = OpenConnection();
        var trains = new List<TrainRecord>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, number, line, origin, destination, duration, operator
                FROM trains
                ORDER BY sort_order, line, number
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                trains.Add(new TrainRecord
                {
                    Id = reader.GetString(0),
                    Number = reader.GetString(1),
                    Line = reader.GetString(2),
                    From = reader.GetString(3),
                    To = reader.GetString(4),
                    Duration = reader.GetString(5),
                    Operator = reader.GetString(6)
                });
            }
        }

        foreach (var train in trains)
        {
            train.Stops = LoadStops(connection, train.Id);
            train.Normalize();
        }

        return trains;
    }

    public void SaveTrains(IEnumerable<TrainRecord> trains)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var clearStops = connection.CreateCommand())
        {
            clearStops.Transaction = transaction;
            clearStops.CommandText = "DELETE FROM stops";
            clearStops.ExecuteNonQuery();
        }

        using (var clearTrains = connection.CreateCommand())
        {
            clearTrains.Transaction = transaction;
            clearTrains.CommandText = "DELETE FROM trains";
            clearTrains.ExecuteNonQuery();
        }

        var index = 0;
        foreach (var train in trains)
        {
            train.Id = string.IsNullOrWhiteSpace(train.Id) ? Guid.NewGuid().ToString("N") : train.Id;
            train.Normalize();
            InsertTrain(connection, transaction, train, index++);

            for (var stopIndex = 0; stopIndex < train.Stops.Count; stopIndex++)
            {
                InsertStop(connection, transaction, train.Id, train.Stops[stopIndex], stopIndex);
            }
        }

        transaction.Commit();
    }

    public bool HasTrains()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM trains LIMIT 1)";
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public List<RoleRecord> LoadRoles()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, description FROM roles ORDER BY name";

        var roles = new List<RoleRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            roles.Add(new RoleRecord
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2)
            });
        }

        return roles;
    }

    public List<UserRecord> LoadUsers()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id, u.username, u.display_name, u.role_id, r.name, u.is_active
            FROM users u
            JOIN roles r ON r.id = u.role_id
            ORDER BY u.username
            """;

        var users = new List<UserRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new UserRecord
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                DisplayName = reader.GetString(2),
                RoleId = reader.GetInt32(3),
                RoleName = reader.GetString(4),
                IsActive = reader.GetInt32(5) == 1
            });
        }

        return users;
    }

    public LoginUser? ValidateLogin(string username, string password)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.username, u.display_name, r.name, u.password_hash
            FROM users u
            JOIN roles r ON r.id = u.role_id
            WHERE lower(u.username) = lower($username) AND u.is_active = 1
            """;
        command.Parameters.AddWithValue("$username", username.Trim());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var storedHash = reader.GetString(3);
        if (!string.Equals(storedHash, HashPassword(password), StringComparison.Ordinal))
        {
            return null;
        }

        return new LoginUser(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    public void SaveRole(RoleRecord role)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        if (role.Id == 0)
        {
            command.CommandText = "INSERT INTO roles (name, description) VALUES ($name, $description)";
        }
        else
        {
            command.CommandText = "UPDATE roles SET name = $name, description = $description WHERE id = $id";
            command.Parameters.AddWithValue("$id", role.Id);
        }

        command.Parameters.AddWithValue("$name", role.Name.Trim());
        command.Parameters.AddWithValue("$description", role.Description.Trim());
        command.ExecuteNonQuery();
    }

    public void SaveUser(UserRecord user, string? password)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        if (user.Id == 0)
        {
            command.CommandText = """
                INSERT INTO users (username, password_hash, role_id, display_name, is_active)
                VALUES ($username, $passwordHash, $roleId, $displayName, $isActive)
                """;
            command.Parameters.AddWithValue("$passwordHash", HashPassword(password ?? "start"));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                command.CommandText = """
                    UPDATE users
                    SET username = $username, role_id = $roleId, display_name = $displayName, is_active = $isActive
                    WHERE id = $id
                    """;
            }
            else
            {
                command.CommandText = """
                    UPDATE users
                    SET username = $username, password_hash = $passwordHash, role_id = $roleId, display_name = $displayName, is_active = $isActive
                    WHERE id = $id
                    """;
                command.Parameters.AddWithValue("$passwordHash", HashPassword(password));
            }

            command.Parameters.AddWithValue("$id", user.Id);
        }

        command.Parameters.AddWithValue("$username", user.Username.Trim());
        command.Parameters.AddWithValue("$roleId", user.RoleId);
        command.Parameters.AddWithValue("$displayName", user.DisplayName.Trim());
        command.Parameters.AddWithValue("$isActive", user.IsActive ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static void EnsureDefaultCredentials(SqliteConnection connection)
    {
        using (var roleCommand = connection.CreateCommand())
        {
            roleCommand.CommandText = """
                INSERT OR IGNORE INTO roles (id, name, description)
                VALUES (1, 'Administrator', 'Vollzugriff auf Datenpflege und Benutzerverwaltung'),
                       (2, 'Datenpflege', 'Pflege von Zugläufen und Exportdaten')
                """;
            roleCommand.ExecuteNonQuery();
        }

        using var userCommand = connection.CreateCommand();
        userCommand.CommandText = """
            INSERT OR IGNORE INTO users (id, username, password_hash, role_id, display_name, is_active)
            VALUES (1, 'admin', $passwordHash, 1, 'Administrator', 1)
            """;
        userCommand.Parameters.AddWithValue("$passwordHash", HashPassword("admin"));
        userCommand.ExecuteNonQuery();
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static List<StopRecord> LoadStops(SqliteConnection connection, string trainId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, arrival, departure, platform
            FROM stops
            WHERE train_id = $trainId
            ORDER BY stop_order
            """;
        command.Parameters.AddWithValue("$trainId", trainId);

        var stops = new List<StopRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            stops.Add(new StopRecord
            {
                Name = reader.GetString(0),
                Arrival = reader.GetString(1),
                Departure = reader.GetString(2),
                Platform = reader.GetString(3)
            });
        }

        return stops;
    }

    private static void InsertTrain(SqliteConnection connection, SqliteTransaction transaction, TrainRecord train, int sortOrder)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO trains (id, number, line, origin, destination, duration, operator, sort_order)
            VALUES ($id, $number, $line, $origin, $destination, $duration, $operator, $sortOrder)
            """;
        command.Parameters.AddWithValue("$id", train.Id);
        command.Parameters.AddWithValue("$number", train.Number);
        command.Parameters.AddWithValue("$line", train.Line);
        command.Parameters.AddWithValue("$origin", train.From);
        command.Parameters.AddWithValue("$destination", train.To);
        command.Parameters.AddWithValue("$duration", train.Duration);
        command.Parameters.AddWithValue("$operator", train.Operator);
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.ExecuteNonQuery();
    }

    private static void InsertStop(SqliteConnection connection, SqliteTransaction transaction, string trainId, StopRecord stop, int sortOrder)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO stops (train_id, stop_order, name, arrival, departure, platform)
            VALUES ($trainId, $stopOrder, $name, $arrival, $departure, $platform)
            """;
        command.Parameters.AddWithValue("$trainId", trainId);
        command.Parameters.AddWithValue("$stopOrder", sortOrder);
        command.Parameters.AddWithValue("$name", stop.Name);
        command.Parameters.AddWithValue("$arrival", stop.Arrival);
        command.Parameters.AddWithValue("$departure", stop.Departure);
        command.Parameters.AddWithValue("$platform", stop.Platform);
        command.ExecuteNonQuery();
    }
}

public sealed record LoginUser(string Username, string DisplayName, string RoleName);

public sealed class RoleRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class UserRecord
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
