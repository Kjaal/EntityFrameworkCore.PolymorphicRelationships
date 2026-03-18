using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace EntityFrameworkCore.PolymorphicRelationships.Tests;

public sealed class PolymorphicProviderIntegrationTests
{
    [Fact]
    public async Task PostgreSql_supports_translated_polymorphic_queries()
    {
        var databaseName = $"polymorphic_pkg_test_{Guid.NewGuid():N}";
        var connectionString = CreatePostgresConnectionString(databaseName);

        if (!await CanConnectToPostgresAsync())
        {
            return;
        }

        try
        {
            await RecreatePostgresDatabaseAsync(databaseName);

            await using var dbContext = new ProviderTestDbContext(
                new DbContextOptionsBuilder<ProviderTestDbContext>()
                    .UseNpgsql(connectionString)
                    .UsePolymorphicRelationships()
                    .Options);

            await dbContext.Database.EnsureCreatedAsync();

            var zPost = new ProviderPost { Id = 1, Title = "Zulu" };
            var aPost = new ProviderPost { Id = 2, Title = "Alpha" };
            var firstComment = new ProviderComment { Id = 10, Body = "Zulu comment" };
            var secondComment = new ProviderComment { Id = 11, Body = "Alpha comment" };
            var thirdComment = new ProviderComment { Id = 12, Body = "Zulu comment 2" };

            dbContext.AddRange(zPost, aPost, firstComment, secondComment, thirdComment);
            dbContext.SetMorphReference(firstComment, nameof(ProviderComment.Commentable), zPost);
            dbContext.SetMorphReference(secondComment, nameof(ProviderComment.Commentable), aPost);
            dbContext.SetMorphReference(thirdComment, nameof(ProviderComment.Commentable), zPost);
            await dbContext.SaveChangesAsync();

            var postsWithComments = await dbContext.Posts.Where(entity => entity.Comments.Any()).ToListAsync();
            var postsWithTwoComments = await dbContext.Posts.Where(entity => entity.Comments.Count >= 2).ToListAsync();
            var orderedBodies = await dbContext.Comments
                .Where(entity => entity.CommentableType == "provider_posts")
                .OrderBy(entity => ((ProviderPost)entity.Commentable!).Title)
                .Select(entity => entity.Body)
                .ToListAsync();
            var filteredBodies = await dbContext.Comments
                .Where(entity => ((ProviderPost)entity.Commentable!).Title == "Alpha")
                .Select(entity => entity.Body)
                .ToListAsync();

            Assert.Equal(2, postsWithComments.Count);
            Assert.Single(postsWithTwoComments);
            Assert.Equal(zPost.Id, postsWithTwoComments[0].Id);
            Assert.Equal(new[] { secondComment.Body, firstComment.Body }, orderedBodies);
            Assert.Equal(new[] { secondComment.Body }, filteredBodies);
        }
        finally
        {
            await DropPostgresDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public void SqlServer_provider_can_build_translated_polymorphic_query()
    {
        var options = new DbContextOptionsBuilder<ProviderTestDbContext>()
            .UseSqlServer(CreateSqlServerConnectionString())
            .UsePolymorphicRelationships()
            .Options;

        using var dbContext = new ProviderTestDbContext(options);

        var query = dbContext.Comments
            .Where(entity => entity.CommentableType == "provider_posts")
            .OrderBy(entity => ((ProviderPost)entity.Commentable!).Title)
            .Select(entity => entity.Body);

        var filteredQuery = dbContext.Comments
            .Where(entity => ((ProviderPost)entity.Commentable!).Title == "Alpha")
            .Select(entity => entity.Body);

        var countQuery = dbContext.Posts
            .Where(entity => entity.Comments.Count >= 2)
            .Select(entity => entity.Id);

        var queryString = query.ToQueryString();
        var filteredQueryString = filteredQuery.ToQueryString();
        var countQueryString = countQuery.ToQueryString();
        Assert.Contains("ORDER BY", queryString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", filteredQueryString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", countQueryString, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePostgresConnectionString(string databaseName)
    {
        var baseConnectionString = GetPostgresBaseConnectionString();

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    private static async Task RecreatePostgresDatabaseAsync(string databaseName)
    {
        var maintenance = GetPostgresBaseConnectionString();

        await using var connection = new NpgsqlConnection(maintenance);
        await connection.OpenAsync();

        await using (var dropCommand = connection.CreateCommand())
        {
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await dropCommand.ExecuteNonQueryAsync();
        }

        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await createCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropPostgresDatabaseAsync(string databaseName)
    {
        var maintenance = GetPostgresBaseConnectionString();

        await using var connection = new NpgsqlConnection(maintenance);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateSqlServerConnectionString()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("POLYMORPHIC_TEST_SQLSERVER")
            ?? "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        };

        return builder.ConnectionString;
    }

    private static async Task<bool> CanConnectToPostgresAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(GetPostgresBaseConnectionString());
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetPostgresBaseConnectionString()
    {
        var environmentConnectionString = Environment.GetEnvironmentVariable("POLYMORPHIC_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(environmentConnectionString))
        {
            return environmentConnectionString;
        }

        var performanceAppSettingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "EntityFrameworkCore.PolymorphicRelationships.PerformanceLab",
            "appsettings.json"));

        if (File.Exists(performanceAppSettingsPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(performanceAppSettingsPath));
            if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
                && connectionStrings.TryGetProperty("Postgres", out var postgres)
                && postgres.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(postgres.GetString()))
            {
                return postgres.GetString()!;
            }
        }

        return "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    }

    private sealed class ProviderTestDbContext(DbContextOptions<ProviderTestDbContext> options) : DbContext(options)
    {
        public DbSet<ProviderPost> Posts => Set<ProviderPost>();

        public DbSet<ProviderComment> Comments => Set<ProviderComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<ProviderPost>("provider_posts");

                polymorphic.Entity<ProviderComment>()
                    .MorphTo(nameof(ProviderComment.Commentable), entity => entity.CommentableType, entity => entity.CommentableId)
                    .MorphMany<ProviderPost>(nameof(ProviderPost.Comments));
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class ProviderPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public List<ProviderComment> Comments { get; set; } = new();
    }

    private sealed class ProviderComment
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? CommentableType { get; set; }

        public int? CommentableId { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public object? Commentable { get; set; }
    }
}
