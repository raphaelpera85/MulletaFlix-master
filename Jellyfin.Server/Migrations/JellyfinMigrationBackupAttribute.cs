using System;

namespace MulletaFlix.Server.Migrations;

/// <summary>
/// Marks an <see cref="MulletaFlixMigrationAttribute"/> migration and instructs the <see cref="MulletaFlixMigrationService"/> to perform a backup.
/// </summary>
[AttributeUsage(System.AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class MulletaFlixMigrationBackupAttribute : System.Attribute
{
    /// <summary>
    /// Gets or Sets a value indicating whether a backup of the old library.db should be performed.
    /// </summary>
    public bool LegacyLibraryDb { get; set; }

    /// <summary>
    /// Gets or Sets a value indicating whether a backup of the Database should be performed.
    /// </summary>
    public bool MulletaFlixDb { get; set; }

    /// <summary>
    /// Gets or Sets a value indicating whether a backup of the metadata folder should be performed.
    /// </summary>
    public bool Metadata { get; set; }

    /// <summary>
    /// Gets or Sets a value indicating whether a backup of the Trickplay folder should be performed.
    /// </summary>
    public bool Trickplay { get; set; }

    /// <summary>
    /// Gets or Sets a value indicating whether a backup of the Subtitles folder should be performed.
    /// </summary>
    public bool Subtitles { get; set; }
}

