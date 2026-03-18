using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Tests;

public sealed class PolymorphicTranslationLimitTests
{
    [Fact]
    public async Task Owner_property_translation_rejects_multi_table_owner_mappings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateTptOwnerContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var post = new TptOwnerPost { Id = 1, Title = "Zulu" };
        var comment = new TptOwnerComment { Id = 10, Body = "Comment" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(TptOwnerComment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await dbContext.Comments
                .Where(entity => entity.CommentableType == "tpt_owner_posts")
                .OrderBy(entity => ((TptOwnerPost)entity.Commentable!).Title)
                .ToListAsync());

        Assert.Contains("single-table mapped entities", exception.Message);
    }

    [Fact]
    public async Task Morph_many_translation_rejects_multi_table_dependent_mappings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateTptDependentContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var post = new TptDependentPost { Id = 1, Title = "Zulu" };
        var comment = new TptDependentComment { Id = 10, Body = "Comment" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(TptDependentComment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await dbContext.Posts
                .Where(entity => entity.Comments.Count > 0)
                .ToListAsync());

        Assert.Contains("single-table mapped entities", exception.Message);
    }

    private static TptOwnerDbContext CreateTptOwnerContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TptOwnerDbContext>()
            .UseSqlite(connection)
            .UsePolymorphicRelationships()
            .Options;

        return new TptOwnerDbContext(options);
    }

    private static TptDependentDbContext CreateTptDependentContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TptDependentDbContext>()
            .UseSqlite(connection)
            .UsePolymorphicRelationships()
            .Options;

        return new TptDependentDbContext(options);
    }

    private sealed class TptOwnerDbContext(DbContextOptions<TptOwnerDbContext> options) : DbContext(options)
    {
        public DbSet<TptOwnerPost> Posts => Set<TptOwnerPost>();

        public DbSet<TptOwnerComment> Comments => Set<TptOwnerComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TptOwnerContentBase>().ToTable("TptOwnerContents");
            modelBuilder.Entity<TptOwnerPost>().ToTable("TptOwnerPosts");

            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<TptOwnerPost>("tpt_owner_posts");

                polymorphic.Entity<TptOwnerComment>()
                    .MorphTo(nameof(TptOwnerComment.Commentable), entity => entity.CommentableType, entity => entity.CommentableId)
                    .MorphMany<TptOwnerPost>(nameof(TptOwnerPost.Comments));
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    private abstract class TptOwnerContentBase
    {
        public int Id { get; set; }
    }

    private sealed class TptOwnerPost : TptOwnerContentBase
    {
        public string Title { get; set; } = string.Empty;

        [NotMapped]
        public List<TptOwnerComment> Comments { get; set; } = new();
    }

    private sealed class TptOwnerComment
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? CommentableType { get; set; }

        public int? CommentableId { get; set; }

        [NotMapped]
        public object? Commentable { get; set; }
    }

    private sealed class TptDependentDbContext(DbContextOptions<TptDependentDbContext> options) : DbContext(options)
    {
        public DbSet<TptDependentPost> Posts => Set<TptDependentPost>();

        public DbSet<TptDependentComment> Comments => Set<TptDependentComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TptDependentCommentBase>().ToTable("TptDependentCommentBases");
            modelBuilder.Entity<TptDependentComment>().ToTable("TptDependentComments");

            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<TptDependentPost>("tpt_dependent_posts");

                polymorphic.Entity<TptDependentComment>()
                    .MorphTo(nameof(TptDependentComment.Commentable), entity => entity.CommentableType, entity => entity.CommentableId)
                    .MorphMany<TptDependentPost>(nameof(TptDependentPost.Comments));
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class TptDependentPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        [NotMapped]
        public List<TptDependentComment> Comments { get; set; } = new();
    }

    private abstract class TptDependentCommentBase
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;
    }

    private sealed class TptDependentComment : TptDependentCommentBase
    {
        public string? CommentableType { get; set; }

        public int? CommentableId { get; set; }

        [NotMapped]
        public object? Commentable { get; set; }
    }
}
