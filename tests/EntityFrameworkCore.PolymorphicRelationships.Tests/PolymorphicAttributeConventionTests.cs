using System.ComponentModel.DataAnnotations.Schema;
using EntityFrameworkCore.PolymorphicRelationships.Attributes;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Tests;

public sealed class PolymorphicAttributeConventionTests
{
    [Fact]
    public async Task Attribute_conventions_register_morph_to_and_inverse_relationships()
    {
        await using var dbContext = CreateAttributeContext();
        var post = new AttributedPost { Id = 1, Title = "Post" };
        var comment = new AttributedComment { Id = 10, Body = "Comment" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(AttributedComment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var owner = await dbContext.LoadMorphAsync<AttributedComment, AttributedPost>(comment, nameof(AttributedComment.Commentable));
        var comments = await dbContext.LoadMorphManyAsync<AttributedPost, AttributedComment>(post, nameof(AttributedPost.Comments));

        Assert.NotNull(owner);
        Assert.Equal(post.Id, owner!.Id);
        Assert.Single(comments);
        Assert.Equal(comment.Id, comments[0].Id);
    }

    [Fact]
    public async Task Attribute_conventions_register_morph_to_many_relationships()
    {
        await using var dbContext = CreateAttributeContext();
        var post = new AttributedPost { Id = 2, Title = "Post" };
        var tag = new AttributedTag { Id = 20, Name = "Tag" };

        dbContext.AddRange(post, tag);
        dbContext.AttachMorphToMany<AttributedPost, AttributedTag, AttributedTagAssignment>(post, nameof(AttributedPost.Tags), tag);
        await dbContext.SaveChangesAsync();

        var tags = await dbContext.LoadMorphToManyAsync<AttributedPost, AttributedTag>(post, nameof(AttributedPost.Tags));
        var posts = await dbContext.LoadMorphedByManyAsync<AttributedTag, AttributedPost>(tag, nameof(AttributedTag.Posts));

        Assert.Single(tags);
        Assert.Single(posts);
        Assert.Equal(tag.Id, tags[0].Id);
        Assert.Equal(post.Id, posts[0].Id);
    }

    [Fact]
    public async Task Typed_morph_load_supports_eager_loading_transforms()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setupContext = CreateAttributeContext(databaseName))
        {
            var post = new AttributedPost
            {
                Id = 3,
                Title = "Post",
                Detail = new AttributedPostDetail { Id = 30, Summary = "Loaded through Include" },
            };
            var comment = new AttributedComment { Id = 31, Body = "Comment" };

            setupContext.AddRange(post, comment);
            setupContext.SetMorphReference(comment, nameof(AttributedComment.Commentable), post);
            await setupContext.SaveChangesAsync();
        }

        await using var eagerContext = CreateAttributeContext(databaseName);
        var reloadedComment = await eagerContext.AttributedComments.SingleAsync();

        var owner = await eagerContext.LoadMorphAsync<AttributedComment, AttributedPost>(
            reloadedComment,
            nameof(AttributedComment.Commentable),
            query => query.Include(post => post.Detail));

        Assert.NotNull(owner);
        Assert.NotNull(owner!.Detail);
        Assert.Equal("Loaded through Include", owner.Detail!.Summary);
    }

    [Fact]
    public async Task LoadMorphsAsync_supports_per_type_eager_loading_plans()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setupContext = CreateAttributeContext(databaseName))
        {
            var post = new AttributedPost
            {
                Id = 4,
                Title = "Post",
                Detail = new AttributedPostDetail { Id = 41, Summary = "Post detail" },
            };
            var video = new AttributedVideo
            {
                Id = 5,
                Title = "Video",
                Detail = new AttributedVideoDetail { Id = 51, Summary = "Video detail" },
            };
            var postComment = new AttributedComment { Id = 42, Body = "Post comment" };
            var videoComment = new AttributedComment { Id = 52, Body = "Video comment" };

            setupContext.AddRange(post, video, postComment, videoComment);
            setupContext.SetMorphReference(postComment, nameof(AttributedComment.Commentable), post);
            setupContext.SetMorphReference(videoComment, nameof(AttributedComment.Commentable), video);
            await setupContext.SaveChangesAsync();
        }

        await using var eagerContext = CreateAttributeContext(databaseName);
        var comments = await eagerContext.AttributedComments.OrderBy(comment => comment.Id).ToListAsync();

        var owners = await eagerContext.LoadMorphsAsync(
            comments,
            nameof(AttributedComment.Commentable),
            plan => plan
                .For<AttributedPost>(query => query.Include(post => post.Detail))
                .For<AttributedVideo>(query => query.Include(video => video.Detail)));

        var loadedPost = Assert.IsType<AttributedPost>(owners[comments[0]]);
        var loadedVideo = Assert.IsType<AttributedVideo>(owners[comments[1]]);
        Assert.NotNull(loadedPost.Detail);
        Assert.NotNull(loadedVideo.Detail);
        Assert.Equal("Post detail", loadedPost.Detail!.Summary);
        Assert.Equal("Video detail", loadedVideo.Detail!.Summary);
    }

    [Fact]
    public async Task IncludeMorph_supports_per_type_eager_loading_plans()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setupContext = CreateAttributeContext(databaseName))
        {
            var post = new AttributedPost
            {
                Id = 6,
                Title = "Post",
                Detail = new AttributedPostDetail { Id = 61, Summary = "Post detail" },
            };
            var comment = new AttributedComment { Id = 62, Body = "Comment" };

            setupContext.AddRange(post, comment);
            setupContext.SetMorphReference(comment, nameof(AttributedComment.Commentable), post);
            await setupContext.SaveChangesAsync();
        }

        await using var eagerContext = CreateAttributeContext(databaseName);

        var includedComment = await eagerContext.AttributedComments
            .IncludeMorph(
                entity => entity.Commentable,
                plan => plan.For<AttributedPost>(query => query.Include(post => post.Detail)))
            .SingleAsync();

        var owner = Assert.IsType<AttributedPost>(includedComment.Commentable);
        Assert.NotNull(owner.Detail);
        Assert.Equal("Post detail", owner.Detail!.Summary);
    }

    [Fact]
    public async Task Designer_helpers_create_laravel_style_shadow_columns()
    {
        await using var dbContext = CreateDesignerContext();
        var post = new DesignerPost { Id = 4, Title = "Post" };
        var comment = new DesignerComment { Id = 40, Body = "Comment" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(DesignerComment.Commentable), post);
        await dbContext.SaveChangesAsync();

        Assert.Equal("posts", dbContext.Entry(comment).Property<string?>("commentable_type").CurrentValue);
        Assert.Equal(4, dbContext.Entry(comment).Property<int>("commentable_id").CurrentValue);
    }

    [Fact]
    public async Task SaveChanges_rejects_partial_morph_reference_values()
    {
        await using var dbContext = CreateAttributeContext();
        dbContext.AttributedComments.Add(new AttributedComment
        {
            Id = 60,
            Body = "Broken",
            CommentableType = "posts",
            CommentableId = null,
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains(nameof(AttributedComment.Commentable), exception.Message);
    }

    [Fact]
    public async Task SaveChanges_rejects_missing_morph_owner()
    {
        await using var dbContext = CreateAttributeContext();
        dbContext.AttributedComments.Add(new AttributedComment
        {
            Id = 61,
            Body = "Missing owner",
            CommentableType = "posts",
            CommentableId = 999,
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("missing owner key", exception.Message);
    }

    [Fact]
    public async Task SaveChanges_rejects_invalid_morph_pivot_rows()
    {
        await using var dbContext = CreateAttributeContext();
        var tag = new AttributedTag { Id = 62, Name = "Tag" };
        var pivot = new AttributedTagAssignment { Id = 63 };
        dbContext.AddRange(tag, pivot);
        dbContext.Entry(pivot).Property("taggable_type").CurrentValue = "posts";
        dbContext.Entry(pivot).Property("taggable_id").CurrentValue = 999;
        dbContext.Entry(pivot).Property("attributed_tag_id").CurrentValue = tag.Id;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("missing owner key", exception.Message);
    }

    private static AttributeDbContext CreateAttributeContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AttributeDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .UsePolymorphicRelationships()
            .Options;

        return new AttributeDbContext(options);
    }

    private static DesignerDbContext CreateDesignerContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<DesignerDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .UsePolymorphicRelationships()
            .Options;

        return new DesignerDbContext(options);
    }

    private sealed class AttributeDbContext(DbContextOptions<AttributeDbContext> options) : DbContext(options)
    {
        public DbSet<AttributedPost> AttributedPosts => Set<AttributedPost>();

        public DbSet<AttributedVideo> AttributedVideos => Set<AttributedVideo>();

        public DbSet<AttributedPostDetail> AttributedPostDetails => Set<AttributedPostDetail>();

        public DbSet<AttributedVideoDetail> AttributedVideoDetails => Set<AttributedVideoDetail>();

        public DbSet<AttributedComment> AttributedComments => Set<AttributedComment>();

        public DbSet<AttributedTag> AttributedTags => Set<AttributedTag>();

        public DbSet<AttributedTagAssignment> AttributedTagAssignments => Set<AttributedTagAssignment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UsePolymorphicRelationshipAttributes();
            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class DesignerDbContext(DbContextOptions<DesignerDbContext> options) : DbContext(options)
    {
        public DbSet<DesignerPost> DesignerPosts => Set<DesignerPost>();

        public DbSet<DesignerComment> DesignerComments => Set<DesignerComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DesignerComment>().HasMorphColumns<int>("commentable");

            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<DesignerPost>("posts");
                polymorphic.Entity<DesignerComment>()
                    .MorphToConvention<int>(nameof(DesignerComment.Commentable))
                    .MorphMany<DesignerPost>(nameof(DesignerPost.Comments));
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    [MorphMap("posts")]
    private sealed class AttributedPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public AttributedPostDetail? Detail { get; set; }

        [NotMapped]
        [MorphMany(typeof(AttributedComment), nameof(AttributedComment.Commentable))]
        public List<AttributedComment> Comments { get; set; } = new();

        [NotMapped]
        [MorphToMany(typeof(AttributedTag), typeof(AttributedTagAssignment), nameof(AttributedTag.Posts), "taggable")]
        public List<AttributedTag> Tags { get; set; } = new();
    }

    [MorphMap("videos")]
    private sealed class AttributedVideo
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public AttributedVideoDetail? Detail { get; set; }

        [NotMapped]
        [MorphMany(typeof(AttributedComment), nameof(AttributedComment.Commentable))]
        public List<AttributedComment> Comments { get; set; } = new();
    }

    private sealed class AttributedPostDetail
    {
        public int Id { get; set; }

        public string Summary { get; set; } = string.Empty;
    }

    private sealed class AttributedVideoDetail
    {
        public int Id { get; set; }

        public string Summary { get; set; } = string.Empty;
    }

    private sealed class AttributedComment
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? CommentableType { get; set; }

        public int? CommentableId { get; set; }

        [NotMapped]
        [MorphTo(nameof(CommentableType))]
        [ForeignKey(nameof(CommentableId))]
        public object? Commentable { get; set; }
    }

    private sealed class AttributedTag
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [NotMapped]
        [MorphedByMany(typeof(AttributedPost), typeof(AttributedTagAssignment), nameof(AttributedPost.Tags), "taggable")]
        public List<AttributedPost> Posts { get; set; } = new();
    }

    private sealed class AttributedTagAssignment
    {
        public int Id { get; set; }

        public string? TaggableType { get; set; }

        public int TaggableId { get; set; }

        public int TagId { get; set; }
    }

    [MorphMap("posts")]
    private sealed class DesignerPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        [NotMapped]
        public List<DesignerComment> Comments { get; set; } = new();
    }

    private sealed class DesignerComment
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;

        [NotMapped]
        public object? Commentable { get; set; }
    }
}

