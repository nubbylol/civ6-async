using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Toggles civ6-async's row in Civ 6's Mods.sqlite to "enabled" so the user
/// doesn't have to open Additional Content → Mods and tick the checkbox.
///
/// Schema (relevant tables):
///   Mods           ModId TEXT (the GUID), ModRowId INTEGER PK
///   ModGroups      ModGroupRowId INTEGER PK, Selected BOOLEAN  (one row per loadout)
///   ModGroupItems  ModGroupRowId, ModRowId, Disabled BOOLEAN   (per-mod-per-group flag)
///
/// Logic: find ModRowId by ModId, find Selected ModGroupRowId, then either
/// UPDATE the existing ModGroupItems row's Disabled=0 or INSERT a new one
/// with Disabled=0.
///
/// Mods.sqlite is populated when Civ scans the Mods folder at startup. So
/// enabling will fail if the mod has been freshly installed and Civ has
/// never been launched since. The caller treats failure as a soft warning.
/// </summary>
internal static class ModEnabler
{
    public const string Civ6AsyncModId = "8758909f-2bc5-4633-9e67-6d7361007c07";

    public enum Result
    {
        Enabled,
        AlreadyEnabled,
        ModNotInDb,        // Civ hasn't scanned the Mods folder yet.
        DbNotFound,
        DbBusy,            // Civ is running and holding the DB lock.
        Error,
    }

    public static (Result Result, string? Message) TryEnable(string? modGuid = null)
    {
        modGuid ??= Civ6AsyncModId;

        var dbPath = PlatformPaths.AutoDetectModsDbPath();
        if (dbPath is null)
        {
            return (Result.DbNotFound,
                "Civ 6's mod database (Mods.sqlite) wasn't found. " +
                "Has Civ been launched on this machine yet?");
        }

        // Refuse to write while Civ is running (it holds the file open).
        if (IsCivRunning())
        {
            return (Result.DbBusy, "Civilization VI is currently running. Close it first.");
        }

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWrite");
            conn.Open();
            using var tx = conn.BeginTransaction();

            long? modRowId = ScalarLong(conn,
                "SELECT ModRowId FROM Mods WHERE ModId = $id",
                ("$id", modGuid));
            if (modRowId is null)
                return (Result.ModNotInDb,
                    "civ6-async isn't in Civ 6's mod database yet. " +
                    "Launch Civ once so it scans the Mods folder, then run install/enable again.");

            long? groupRowId = ScalarLong(conn,
                "SELECT ModGroupRowId FROM ModGroups WHERE Selected = 1 LIMIT 1");
            if (groupRowId is null)
                return (Result.Error, "No selected mod group found in Mods.sqlite.");

            var existing = ScalarLong(conn,
                "SELECT Disabled FROM ModGroupItems WHERE ModGroupRowId = $g AND ModRowId = $m",
                ("$g", groupRowId.Value), ("$m", modRowId.Value));

            if (existing is null)
            {
                Exec(conn,
                    "INSERT INTO ModGroupItems (ModGroupRowId, ModRowId, Disabled) VALUES ($g, $m, 0)",
                    ("$g", groupRowId.Value), ("$m", modRowId.Value));
            }
            else if (existing.Value == 0)
            {
                tx.Commit();
                return (Result.AlreadyEnabled, null);
            }
            else
            {
                Exec(conn,
                    "UPDATE ModGroupItems SET Disabled = 0 WHERE ModGroupRowId = $g AND ModRowId = $m",
                    ("$g", groupRowId.Value), ("$m", modRowId.Value));
            }

            tx.Commit();
            return (Result.Enabled, null);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 /* SQLITE_BUSY */)
        {
            return (Result.DbBusy,
                "Mods.sqlite is locked. Close Civilization VI and any related processes, then retry.");
        }
        catch (Exception ex)
        {
            return (Result.Error, ex.Message);
        }
    }

    private static bool IsCivRunning()
    {
        try
        {
            // Common process names across launchers + platforms.
            string[] names =
            {
                "CivilizationVI",
                "CivilizationVI_DX12",
                "Civ6",
                "Civilization Vi",
            };
            foreach (var n in names)
                if (Process.GetProcessesByName(n).Length > 0) return true;
        }
        catch { }
        return false;
    }

    private static long? ScalarLong(SqliteConnection conn, string sql, params (string Name, object Value)[] parms)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        foreach (var p in parms) c.Parameters.AddWithValue(p.Name, p.Value);
        var v = c.ExecuteScalar();
        if (v is null || v is DBNull) return null;
        return Convert.ToInt64(v);
    }

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] parms)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        foreach (var p in parms) c.Parameters.AddWithValue(p.Name, p.Value);
        c.ExecuteNonQuery();
    }
}
