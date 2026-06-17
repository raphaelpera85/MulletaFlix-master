using System.Collections.ObjectModel;

namespace MulletaFlix.Server.Migrations.Stages;

/// <summary>
/// Defines a Stage that can be Invoked and Handled at different times from the code.
/// </summary>
internal class MigrationStage : Collection<CodeMigration>
{
    public MigrationStage(MulletaFlixMigrationStageTypes stage)
    {
        Stage = stage;
    }

    public MulletaFlixMigrationStageTypes Stage { get; }
}

