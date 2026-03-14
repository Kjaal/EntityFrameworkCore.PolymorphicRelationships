using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using EntityFrameworkCore.PolymorphicRelationships.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.PolymorphicRelationships.Tests;

public sealed class MigrationScaffoldingTests
{
    [Fact]
    public void Designer_helpers_are_scaffolded_into_migration_output()
    {
        using var dbContext = CreateDesignerMigrationContext();
        var scaffoldedMigration = ScaffoldMigration(dbContext, nameof(Designer_helpers_are_scaffolded_into_migration_output));

        Assert.Contains("commentable_type", scaffoldedMigration.MigrationCode);
        Assert.Contains("commentable_id", scaffoldedMigration.MigrationCode);
        Assert.Contains("taggable_type", scaffoldedMigration.MigrationCode);
        Assert.Contains("taggable_id", scaffoldedMigration.MigrationCode);
        Assert.Contains("migration_tag_id", scaffoldedMigration.MigrationCode);
        Assert.Contains("commentable_type", scaffoldedMigration.SnapshotCode);
        Assert.Contains("commentable_id", scaffoldedMigration.SnapshotCode);
        Assert.Contains("taggable_type", scaffoldedMigration.SnapshotCode);
        Assert.Contains("taggable_id", scaffoldedMigration.SnapshotCode);
        Assert.Contains("migration_tag_id", scaffoldedMigration.SnapshotCode);
    }

    [Fact]
    public void Attribute_conventions_are_scaffolded_into_model_snapshot()
    {
        using var dbContext = CreateAttributeMigrationContext();
        var scaffoldedMigration = ScaffoldMigration(dbContext, nameof(Attribute_conventions_are_scaffolded_into_model_snapshot));

        Assert.Contains("CommentableType", scaffoldedMigration.SnapshotCode);
        Assert.Contains("CommentableId", scaffoldedMigration.SnapshotCode);
        Assert.Contains("taggable_type", scaffoldedMigration.SnapshotCode);
        Assert.Contains("taggable_id", scaffoldedMigration.SnapshotCode);
        Assert.Contains("migration_tag_id", scaffoldedMigration.SnapshotCode);
        Assert.Contains("HasIndex", scaffoldedMigration.SnapshotCode);
    }

    private static (string MigrationCode, string SnapshotCode) ScaffoldMigration(DbContext dbContext, string migrationName)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddEntityFrameworkDesignTimeServices();
        serviceCollection.AddDbContextDesignTimeServices(dbContext);

        var providerName = dbContext.Database.ProviderName
            ?? throw new InvalidOperationException("The DbContext does not expose a configured provider name.");

        var providerAssembly = Assembly.Load(new AssemblyName(providerName));
        var providerServicesAttribute = providerAssembly.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException("The current provider does not expose design-time services.");

        var providerServicesType =
            Type.GetType(providerServicesAttribute.TypeName, throwOnError: false)
            ?? providerAssembly.GetType(providerServicesAttribute.TypeName, throwOnError: false)
            ?? LoadDesignAssembly(providerName).GetType(providerServicesAttribute.TypeName, throwOnError: false)
            ?? throw new InvalidOperationException($"Unable to load provider design-time services '{providerServicesAttribute.TypeName}'.");

        var providerServices = (IDesignTimeServices?)Activator.CreateInstance(providerServicesType)
            ?? throw new InvalidOperationException($"Unable to create provider design-time services '{providerServicesType.FullName}'.");

        providerServices.ConfigureDesignTimeServices(serviceCollection);

        var scaffolder = serviceCollection
            .BuildServiceProvider()
            .GetRequiredService<IMigrationsScaffolder>();

        var scaffoldedMigration = scaffolder.ScaffoldMigration(migrationName, typeof(MigrationScaffoldingTests).Namespace!);
        var migrationCode = (string?)scaffoldedMigration.GetType().GetProperty("MigrationCode")?.GetValue(scaffoldedMigration)
            ?? throw new InvalidOperationException("The generated migration did not expose MigrationCode.");
        var snapshotCode = (string?)scaffoldedMigration.GetType().GetProperty("SnapshotCode")?.GetValue(scaffoldedMigration)
            ?? throw new InvalidOperationException("The generated migration did not expose SnapshotCode.");

        return (migrationCode, snapshotCode);
    }

    private static Assembly LoadDesignAssembly(string providerName)
    {
        return Assembly.Load(new AssemblyName($"{providerName}.Design"));
    }

    private static DesignerMigrationDbContext CreateDesignerMigrationContext()
    {
        var options = new DbContextOptionsBuilder<DesignerMigrationDbContext>()
            .UseSqlite("Data Source=designer-migration.db")
            .UsePolymorphicRelationships()
            .Options;

        return new DesignerMigrationDbContext(options);
    }

    private static AttributeMigrationDbContext CreateAttributeMigrationContext()
    {
        var options = new DbContextOptionsBuilder<AttributeMigrationDbContext>()
            .UseSqlite("Data Source=attribute-migration.db")
            .UsePolymorphicRelationships()
            .Options;

        return new AttributeMigrationDbContext(options);
    }

    private sealed class DesignerMigrationDbContext(DbContextOptions<DesignerMigrationDbContext> options) : DbContext(options)
    {
        public DbSet<MigrationPost> Posts => Set<MigrationPost>();

        public DbSet<MigrationComment> Comments => Set<MigrationComment>();

        public DbSet<MigrationTag> Tags => Set<MigrationTag>();

        public DbSet<MigrationTaggable> Taggables => Set<MigrationTaggable>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MigrationComment>().HasMorphColumns<int>("commentable");
            modelBuilder.Entity<MigrationTaggable>().HasMorphToManyColumns<int, int>("taggable", typeof(MigrationTag));

            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<MigrationPost>("posts");
                polymorphic.Entity<MigrationComment>()
                    .MorphToConvention<int>(nameof(MigrationComment.Commentable))
                    .MorphMany<MigrationPost>(nameof(MigrationPost.Comments));

                polymorphic.MorphToManyConvention<MigrationPost, MigrationTag, MigrationTaggable, int, int>(
                    nameof(MigrationPost.Tags),
                    nameof(MigrationTag.Posts),
                    "taggable");
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class AttributeMigrationDbContext(DbContextOptions<AttributeMigrationDbContext> options) : DbContext(options)
    {
        public DbSet<AttributedMigrationPost> Posts => Set<AttributedMigrationPost>();

        public DbSet<AttributedMigrationComment> Comments => Set<AttributedMigrationComment>();

        public DbSet<AttributedMigrationTag> Tags => Set<AttributedMigrationTag>();

        public DbSet<AttributedMigrationTaggable> Taggables => Set<AttributedMigrationTaggable>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UsePolymorphicRelationshipAttributes();
            base.OnModelCreating(modelBuilder);
        }
    }

    [MorphMap("posts")]
    private sealed class AttributedMigrationPost
    {
        public int Id { get; set; }

        [NotMapped]
        [MorphMany(typeof(AttributedMigrationComment), nameof(AttributedMigrationComment.Commentable))]
        public List<AttributedMigrationComment> Comments { get; set; } = new();

        [NotMapped]
        [MorphToMany(typeof(AttributedMigrationTag), typeof(AttributedMigrationTaggable), nameof(AttributedMigrationTag.Posts), "taggable")]
        public List<AttributedMigrationTag> Tags { get; set; } = new();
    }

    private sealed class AttributedMigrationComment
    {
        public int Id { get; set; }

        public string? CommentableType { get; set; }

        public int? CommentableId { get; set; }

        [NotMapped]
        [MorphTo(nameof(CommentableType))]
        [ForeignKey(nameof(CommentableId))]
        public object? Commentable { get; set; }
    }

    private sealed class AttributedMigrationTag
    {
        public int Id { get; set; }

        [NotMapped]
        [MorphedByMany(typeof(AttributedMigrationPost), typeof(AttributedMigrationTaggable), nameof(AttributedMigrationPost.Tags), "taggable")]
        public List<AttributedMigrationPost> Posts { get; set; } = new();
    }

    private sealed class AttributedMigrationTaggable
    {
        public int Id { get; set; }
    }

    [MorphMap("posts")]
    private sealed class MigrationPost
    {
        public int Id { get; set; }

        [NotMapped]
        public List<MigrationComment> Comments { get; set; } = new();

        [NotMapped]
        public List<MigrationTag> Tags { get; set; } = new();
    }

    private sealed class MigrationComment
    {
        public int Id { get; set; }

        [NotMapped]
        public object? Commentable { get; set; }
    }

    private sealed class MigrationTag
    {
        public int Id { get; set; }

        [NotMapped]
        public List<MigrationPost> Posts { get; set; } = new();
    }

    private sealed class MigrationTaggable
    {
        public int Id { get; set; }
    }
}

