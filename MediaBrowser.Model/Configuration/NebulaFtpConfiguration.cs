namespace MediaBrowser.Model.Configuration;

public class NebulaFtpConfiguration
{
    public bool Enabled { get; set; }

    public string RaiDriveDownloadUrl { get; set; } = "https://www.raidrive.com/download";

    public string ServerHost { get; set; } = "0.0.0.0";

    public int ServerPort { get; set; } = 2121;

    public string PassivePorts { get; set; } = "60000-60009";

    public string MongoDbConnectionString { get; set; } = "mongodb://localhost:27017";

    public string ApiId { get; set; } = "38735893";

    public string ApiHash { get; set; } = "9fdff98d953c3989c4f25a534688f810";

    public string ChatId { get; set; } = "-1004391811380";

    public string BotTokens { get; set; } = "8475077434:AAH-Q_suTBDZ69yvyq-s8pX51oQ0STdqad0,8743388135:AAHLqOlUyqts0TxagxSeGaVXplbB7RHOlw0,8890265800:AAEJITMzDhEGt2WHYx2MnXFk79NO6uPd804,8945326512:AAGwk5VUj--puMb0IEBpPk7CTOeeZZyovoM,8722157038:AAEbjRFss6VnDYVTctD9NVUksFsEPqGLt9I,8756027175:AAGS7f6dbNrJRq_FynVDjIdB2OoeBeVhhlQ,8877238019:AAGzllsMvlqByvX5xvaFXWG-Ze2cKOTlZOw,8980197230:AAF7uZ6GvK_RntQgQ6lx6_gHe6v4ZqXFRKs,8997013483:AAH1syut1zb8ZlVCnDpid11PD8BFnjeU8Fo,8755896032:AAEtcS4fHh6eop5ppWaiYF7hTuMDwYPvNlk";

    public int MaxWorkers { get; set; } = 10;

    public int ChunkSizeMb { get; set; } = 64;

    public bool DeleteSourceAfterUpload { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DriveLetter { get; set; } = "Z:";

    public string RemotePath { get; set; } = "/";

    public bool UseMappedDrive { get; set; } = true;

    public string WatchFolderPath { get; set; } = string.Empty;

    public string SetupNotes { get; set; } = "Use o botao Baixar RaiDrive para montar o NebulaFTP como unidade no Windows.";
}
