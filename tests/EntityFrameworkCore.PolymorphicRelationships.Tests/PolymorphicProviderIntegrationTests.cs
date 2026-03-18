using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace EntityFrameworkCore.PolymorphicRelationships.Tests;

public sealed class PolymorphicProviderIntegrationTests
{
    [Fact]
    public async Task PostgreSql_executes_runtime_and_translated_polymorphic_queries()
    {
        await RunProviderScenarioAsync(ProviderBackend.PostgreSql);
    }

    [Fact]
    public async Task PostgreSql_enforces_uniqueness_and_loaded_removal_sync()
    {
        await RunProviderIntegrityScenarioAsync(ProviderBackend.PostgreSql);
    }

    [Fact]
    public async Task SqlServer_executes_runtime_and_translated_polymorphic_queries()
    {
        await RunProviderScenarioAsync(ProviderBackend.SqlServer);
    }

    [Fact]
    public async Task SqlServer_enforces_uniqueness_and_loaded_removal_sync()
    {
        await RunProviderIntegrityScenarioAsync(ProviderBackend.SqlServer);
    }

    [Fact]
    public async Task MySql_executes_runtime_and_translated_polymorphic_queries()
    {
        await RunProviderScenarioAsync(ProviderBackend.MySql);
    }

    [Fact]
    public async Task MySql_enforces_uniqueness_and_loaded_removal_sync()
    {
        await RunProviderIntegrityScenarioAsync(ProviderBackend.MySql);
    }

    private static async Task RunProviderScenarioAsync(ProviderBackend backend)
    {
        var databaseName = $"poly_pkg_{Guid.NewGuid():N}";

        if (!await CanConnectAsync(backend))
        {
            EnsureProviderAvailabilityOrSkip(backend);
            return;
        }

        try
        {
            await RecreateDatabaseAsync(backend, databaseName);

            var connectionString = CreateConnectionString(backend, databaseName);
            await using var dbContext = new ProviderTestDbContext(CreateOptions(backend, connectionString));
            await dbContext.Database.EnsureCreatedAsync();

            var zuluPost = new ProviderPost { Title = "Zulu" };
            var alphaPost = new ProviderPost { Title = "Alpha" };
            var sharedTag = new ProviderTag { Name = "shared" };
            var firstComment = new ProviderComment { Body = "Zulu comment" };
            var secondComment = new ProviderComment { Body = "Alpha comment" };
            var thirdComment = new ProviderComment { Body = "Zulu comment 2" };

            dbContext.AddRange(zuluPost, alphaPost, sharedTag, firstComment, secondComment, thirdComment);
            dbContext.SetMorphReference(firstComment, nameof(ProviderComment.Commentable), zuluPost);
            dbContext.SetMorphReference(secondComment, nameof(ProviderComment.Commentable), alphaPost);
            dbContext.SetMorphReference(thirdComment, nameof(ProviderComment.Commentable), zuluPost);
            dbContext.AttachMorphToMany<ProviderPost, ProviderTag, ProviderTagAssignment>(zuluPost, nameof(ProviderPost.Tags), sharedTag);
            await dbContext.SaveChangesAsync();

            Assert.True(zuluPost.Id > 0);
            Assert.True(alphaPost.Id > 0);
            Assert.True(sharedTag.Id > 0);
            Assert.Equal(zuluPost.Id, firstComment.CommentableId);
            Assert.Equal(alphaPost.Id, secondComment.CommentableId);

            var loadedOwner = await dbContext.LoadMorphAsync<ProviderComment, ProviderPost>(secondComment, nameof(ProviderComment.Commentable));
            var loadedTags = await dbContext.LoadMorphToManyAsync<ProviderPost, ProviderTag>(zuluPost, nameof(ProviderPost.Tags));
            var includedPost = await dbContext.Posts
                .IncludeMorph(entity => entity.Comments)
                .Where(entity => entity.Id == zuluPost.Id)
                .SingleAsync();

            var postsWithComments = await dbContext.Posts.Where(entity => entity.Comments.Any()).OrderBy(entity => entity.Title).ToListAsync();
            var postsWithTwoComments = await dbContext.Posts.Where(entity => entity.Comments.Count >= 2).ToListAsync();
            var orderedBodies = await dbContext.Comments
                .Where(entity => entity.CommentableType == "provider_posts")
                .OrderBy(entity => ((ProviderPost)entity.Commentable!).Title)
                .ThenBy(entity => entity.Body)
                .Select(entity => entity.Body)
                .ToListAsync();
            var filteredBodies = await dbContext.Comments
                .Where(entity => ((ProviderPost)entity.Commentable!).Title == "Alpha")
                .Select(entity => entity.Body)
                .ToListAsync();

            Assert.NotNull(loadedOwner);
            Assert.Equal(alphaPost.Id, loadedOwner!.Id);
            Assert.Single(loadedTags);
            Assert.Equal(sharedTag.Id, loadedTags[0].Id);
            Assert.Equal(2, includedPost.Comments.Count);
            Assert.Equal(2, postsWithComments.Count);
            Assert.Single(postsWithTwoComments);
            Assert.Equal(zuluPost.Id, postsWithTwoComments[0].Id);
            Assert.Equal(new[] { secondComment.Body, firstComment.Body, thirdComment.Body }, orderedBodies);
            Assert.Equal(new[] { secondComment.Body }, filteredBodies);
        }
        finally
        {
            await DropDatabaseAsync(backend, databaseName);
        }
    }

    private static async Task RunProviderIntegrityScenarioAsync(ProviderBackend backend)
    {
        var databaseName = $"poly_pkg_integrity_{Guid.NewGuid():N}";

        if (!await CanConnectAsync(backend))
        {
            EnsureProviderAvailabilityOrSkip(backend);
            return;
        }

        try
        {
            await RecreateDatabaseAsync(backend, databaseName);

            var connectionString = CreateConnectionString(backend, databaseName);
            await using var dbContext = new ProviderTestDbContext(CreateOptions(backend, connectionString));
            await dbContext.Database.EnsureCreatedAsync();

            var post = new ProviderPost { Title = "Owner" };
            var firstComment = new ProviderComment { Body = "First" };
            var secondComment = new ProviderComment { Body = "Second" };
            var tag = new ProviderTag { Name = "shared" };

            dbContext.AddRange(post, firstComment, secondComment, tag);
            dbContext.SetMorphReference(firstComment, nameof(ProviderComment.Commentable), post);
            dbContext.SetMorphReference(secondComment, nameof(ProviderComment.Commentable), post);
            dbContext.AttachMorphToMany<ProviderPost, ProviderTag, ProviderTagAssignment>(post, nameof(ProviderPost.Tags), tag);
            await dbContext.SaveChangesAsync();

            await dbContext.LoadMorphManyAsync<ProviderPost, ProviderComment>(
                post,
                nameof(ProviderPost.Comments),
                query => query.Where(entity => entity.Id == firstComment.Id));

            post.Comments.Clear();
            dbContext.AttachMorphToMany<ProviderPost, ProviderTag, ProviderTagAssignment>(post, nameof(ProviderPost.Tags), tag);
            await dbContext.SaveChangesAsync();

            var reloadedFirst = await dbContext.Comments.SingleAsync(entity => entity.Id == firstComment.Id);
            var reloadedSecond = await dbContext.Comments.SingleAsync(entity => entity.Id == secondComment.Id);
            var pivots = await dbContext.TagAssignments.ToListAsync();

            Assert.Null(reloadedFirst.CommentableType);
            Assert.Null(reloadedFirst.CommentableId);
            Assert.Equal("provider_posts", reloadedSecond.CommentableType);
            Assert.Equal(post.Id, reloadedSecond.CommentableId);
            Assert.Single(pivots);

            var firstImage = new ProviderImage { Url = "/first.png" };
            var secondImage = new ProviderImage { Url = "/second.png" };
            dbContext.AddRange(firstImage, secondImage);
            dbContext.SetMorphReference(firstImage, nameof(ProviderImage.Imageable), post);
            dbContext.SetMorphReference(secondImage, nameof(ProviderImage.Imageable), post);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
            Assert.Contains("allows only one dependent", exception.Message);
        }
        finally
        {
            await DropDatabaseAsync(backend, databaseName);
        }
    }

    private static DbContextOptions<ProviderTestDbContext> CreateOptions(ProviderBackend backend, string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ProviderTestDbContext>();

        switch (backend)
        {
            case ProviderBackend.PostgreSql:
                builder.UseNpgsql(connectionString);
                break;
            case ProviderBackend.SqlServer:
                builder.UseSqlServer(connectionString);
                break;
            case ProviderBackend.MySql:
                builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, null);
        }

        builder.UsePolymorphicRelationships();
        return builder.Options;
    }

    private static async Task<bool> CanConnectAsync(ProviderBackend backend)
    {
        try
        {
            switch (backend)
            {
                case ProviderBackend.PostgreSql:
                    await using (var connection = new NpgsqlConnection(GetPostgresBaseConnectionString()))
                    {
                        await connection.OpenAsync();
                    }

                    return true;
                case ProviderBackend.SqlServer:
                    await using (var connection = new SqlConnection(GetSqlServerBaseConnectionString()))
                    {
                        await connection.OpenAsync();
                    }

                    return true;
                case ProviderBackend.MySql:
                    await using (var connection = new MySqlConnection(GetMySqlBaseConnectionString()))
                    {
                        await connection.OpenAsync();
                    }

                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureProviderAvailabilityOrSkip(ProviderBackend backend)
    {
        if (!string.IsNullOrWhiteSpace(GetProviderConnectionOverride(backend)))
        {
            throw new InvalidOperationException($"The {backend} provider test environment is required but could not be reached.");
        }
    }

    private static string? GetProviderConnectionOverride(ProviderBackend backend)
    {
        return backend switch
        {
            ProviderBackend.PostgreSql => Environment.GetEnvironmentVariable("POLYMORPHIC_TEST_POSTGRES"),
            ProviderBackend.SqlServer => Environment.GetEnvironmentVariable("POLYMORPHIC_TEST_SQLSERVER"),
            ProviderBackend.MySql => Environment.GetEnvironmentVariable("POLYMORPHIC_TEST_MYSQL"),
            _ => null,
        };
    }

    private static Task RecreateDatabaseAsync(ProviderBackend backend, string databaseName)
    {
        return backend switch
        {
            ProviderBackend.PostgreSql => RecreatePostgresDatabaseAsync(databaseName),
            ProviderBackend.SqlServer => RecreateSqlServerDatabaseAsync(databaseName),
            ProviderBackend.MySql => RecreateMySqlDatabaseAsync(databaseName),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null),
        };
    }

    private static Task DropDatabaseAsync(ProviderBackend backend, string databaseName)
    {
        return backend switch
        {
            ProviderBackend.PostgreSql => DropPostgresDatabaseAsync(databaseName),
            ProviderBackend.SqlServer => DropSqlServerDatabaseAsync(databaseName),
            ProviderBackend.MySql => DropMySqlDatabaseAsync(databaseName),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null),
        };
    }

    private static string CreateConnectionString(ProviderBackend backend, string databaseName)
    {
        return backend switch
        {
            ProviderBackend.PostgreSql => CreatePostgresConnectionString(databaseName),
            ProviderBackend.SqlServer => CreateSqlServerConnectionString(databaseName),
            ProviderBackend.MySql => CreateMySqlConnectionString(databaseName),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null),
        };
    }

    private static string CreatePostgresConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetPostgresBaseConnectionString())
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    private static async Task RecreatePostgresDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(GetPostgresBaseConnectionString());
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
        await using var connection = new NpgsqlConnection(GetPostgresBaseConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateSqlServerConnectionString(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(GetSqlServerBaseConnectionString())
        {
            InitialCatalog = databaseName,
        };

        return builder.ConnectionString;
    }

    private static async Task RecreateSqlServerDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(GetSqlServerBaseConnectionString());
        await connection.OpenAsync();

        await using (var dropCommand = connection.CreateCommand())
        {
            dropCommand.CommandText = $@"
IF DB_ID(N'{databaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{databaseName}];
END";
            await dropCommand.ExecuteNonQueryAsync();
        }

        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE DATABASE [{databaseName}];";
            await createCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropSqlServerDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(GetSqlServerBaseConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
IF DB_ID(N'{databaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{databaseName}];
END";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateMySqlConnectionString(string databaseName)
    {
        var builder = new MySqlConnectionStringBuilder(GetMySqlBaseConnectionString())
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    private static async Task RecreateMySqlDatabaseAsync(string databaseName)
    {
        await using var connection = new MySqlConnection(GetMySqlBaseConnectionString());
        await connection.OpenAsync();

        await using (var dropCommand = connection.CreateCommand())
        {
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
            await dropCommand.ExecuteNonQueryAsync();
        }

        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE DATABASE `{databaseName}`;";
            await createCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropMySqlDatabaseAsync(string databaseName)
    {
        await using var connection = new MySqlConnection(GetMySqlBaseConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`;";
        await command.ExecuteNonQueryAsync();
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

    private static string GetSqlServerBaseConnectionString()
    {
        return Environment.GetEnvironmentVariable("POLYMORPHIC_TEST_SQLSERVER")
            ?? "Server=localhost,1433;User ID=sa;Password=Strong!Passw0rd;TrustServerCertificate=True;Encrypt=False;Database=master";
    }

    private static string GetMySqlBaseConnectionString()
    {
        return Environment.GetEnvironmentVariable("POLYMORPHIC_TEST_MYSQL")
            ?? "Server=localhost;Port=3306;Database=mysql;User=root;Password=mysql;SslMode=None;AllowPublicKeyRetrieval=True";
    }

    private sealed class ProviderTestDbContext(DbContextOptions<ProviderTestDbContext> options) : DbContext(options)
    {
        public DbSet<ProviderPost> Posts => Set<ProviderPost>();

        public DbSet<ProviderComment> Comments => Set<ProviderComment>();

        public DbSet<ProviderImage> Images => Set<ProviderImage>();

        public DbSet<ProviderTag> Tags => Set<ProviderTag>();

        public DbSet<ProviderTagAssignment> TagAssignments => Set<ProviderTagAssignment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<ProviderPost>("provider_posts");

                polymorphic.Entity<ProviderComment>()
                    .MorphTo(nameof(ProviderComment.Commentable), entity => entity.CommentableType, entity => entity.CommentableId)
                    .MorphMany<ProviderPost>(nameof(ProviderPost.Comments));

                polymorphic.Entity<ProviderImage>()
                    .MorphTo(nameof(ProviderImage.Imageable), entity => entity.ImageableType, entity => entity.ImageableId)
                    .MorphOne<ProviderPost>(nameof(ProviderPost.Image));

                polymorphic.MorphToMany<ProviderPost, ProviderTag, ProviderTagAssignment, int, int>(
                    nameof(ProviderPost.Tags),
                    nameof(ProviderTag.Posts),
                    entity => entity.TaggableType,
                    entity => entity.TaggableId,
                    entity => entity.TagId);
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class ProviderPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        [NotMapped]
        public List<ProviderComment> Comments { get; set; } = new();

        [NotMapped]
        public List<ProviderTag> Tags { get; set; } = new();

        [NotMapped]
        public ProviderImage? Image { get; set; }
    }

    private sealed class ProviderComment
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? CommentableType { get; set; }

        public int? CommentableId { get; set; }

        [NotMapped]
        public object? Commentable { get; set; }
    }

    private sealed class ProviderTag
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [NotMapped]
        public List<ProviderPost> Posts { get; set; } = new();
    }

    private sealed class ProviderImage
    {
        public int Id { get; set; }

        public string Url { get; set; } = string.Empty;

        public string? ImageableType { get; set; }

        public int? ImageableId { get; set; }

        [NotMapped]
        public object? Imageable { get; set; }
    }

    private sealed class ProviderTagAssignment
    {
        public int Id { get; set; }

        public string? TaggableType { get; set; }

        public int TaggableId { get; set; }

        public int TagId { get; set; }
    }

    private enum ProviderBackend
    {
        PostgreSql,
        SqlServer,
        MySql,
    }
}
