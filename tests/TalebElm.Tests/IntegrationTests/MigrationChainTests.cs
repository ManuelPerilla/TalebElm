using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TalebElm.Infrastructure.Persistence;

namespace TalebElm.Tests.IntegrationTests;

public class MigrationChainTests
{
    [Fact]
    public async Task MigrateAsync_FromEmptyDatabase_CreatesExpectedSchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TalebElm.Migrations.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "migrations.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .Options;
            await using var context = new AppDbContext(options);

            Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
            var migrations = context.Database.GetMigrations().ToArray();
            Assert.NotEmpty(migrations);

            await context.Database.MigrateAsync();

            Assert.Equal(migrations, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());

            var expectedColumns = new Dictionary<string, string[]>
            {
                ["Users"] = ["Email", "PasswordHash", "Role"],
                ["Tracks"] = ["Name", "Status"],
                ["Modules"] = ["TrackId", "Order"],
                ["Lessons"] = ["ModuleId", "Content"],
                ["LessonResources"] = ["LessonId", "Kind"],
                ["Exams"] = ["ModuleId", "PassThreshold"],
                ["UserProgresses"] = ["UserId", "ModuleId", "PassedExam", "Score"],
                ["TrackEnrollments"] = ["UserId", "TrackId", "EnrolledAt"]
            };
            foreach (var (table, expected) in expectedColumns)
            {
                var columns = await ReadNamesAsync(connection,
                    "SELECT name FROM pragma_table_info($name)", table);
                Assert.Contains("Id", columns);
                Assert.Contains("CreatedAt", columns);
                foreach (var column in expected)
                {
                    Assert.Contains(column, columns);
                }
            }

            await AssertUniqueIndexAsync(connection, "UserProgresses",
                "IX_UserProgresses_UserId_ModuleId", ["UserId", "ModuleId"]);
            await AssertUniqueIndexAsync(connection, "TrackEnrollments",
                "IX_TrackEnrollments_UserId_TrackId", ["UserId", "TrackId"]);
        }
        finally
        {
            // Pooling is disabled and both context and connection are disposed before cleanup.
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertUniqueIndexAsync(
        SqliteConnection connection, string table, string index, string[] columns)
    {
        var uniqueIndexes = await ReadNamesAsync(connection,
            "SELECT name FROM pragma_index_list($name) WHERE \"unique\" = 1 AND partial = 0", table);
        Assert.Contains(index, uniqueIndexes);

        var indexColumns = await ReadNamesAsync(connection,
            "SELECT name FROM pragma_index_info($name) ORDER BY seqno", index);
        Assert.Equal(columns, indexColumns);
    }

    private static async Task<string[]> ReadNamesAsync(SqliteConnection connection, string sql, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$name", name);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }
}
