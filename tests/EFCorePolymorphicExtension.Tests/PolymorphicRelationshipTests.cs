using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EFCorePolymorphicExtension.Tests;

public sealed class PolymorphicRelationshipTests
{
    [Fact]
    public async Task SetMorphReference_populates_laravel_style_type_and_id_columns()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 10, Title = "Hello" };
        var comment = new Comment { Id = 200, Body = "Nice post" };

        dbContext.Add(post);
        dbContext.Add(comment);
        dbContext.SetMorphReference(comment, nameof(Comment.Commentable), post);

        await dbContext.SaveChangesAsync();

        Assert.Equal("posts", comment.CommentableType);
        Assert.Equal(10, comment.CommentableId);

        var owner = await dbContext.LoadMorphAsync<Comment, Post>(comment, nameof(Comment.Commentable));

        Assert.NotNull(owner);
        Assert.Equal(post.Id, owner!.Id);
        Assert.Same(owner, comment.Commentable);
    }

    [Fact]
    public async Task LoadMorphMany_returns_only_dependents_for_the_selected_owner()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 1, Title = "Post" };
        var video = new Video { Id = 2, Title = "Video" };
        var postComment = new Comment { Id = 101, Body = "Post comment" };
        var videoComment = new Comment { Id = 102, Body = "Video comment" };

        dbContext.AddRange(post, video, postComment, videoComment);
        dbContext.SetMorphReference(postComment, nameof(Comment.Commentable), post);
        dbContext.SetMorphReference(videoComment, nameof(Comment.Commentable), video);
        await dbContext.SaveChangesAsync();

        var loadedComments = await dbContext.LoadMorphManyAsync<Post, Comment>(post, nameof(Post.Comments));

        Assert.Single(loadedComments);
        Assert.Equal(postComment.Id, loadedComments[0].Id);
        Assert.Single(post.Comments);
        Assert.Equal(postComment.Id, post.Comments[0].Id);
    }

    [Fact]
    public async Task Cascade_delete_is_executed_in_code_for_registered_morph_relationships()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setupContext = CreateContext(databaseName))
        {
            var post = new Post { Id = 4, Title = "Delete me" };
            var image = new Image { Id = 22, Url = "/cover.png" };
            var comment = new Comment { Id = 23, Body = "Child" };

            setupContext.AddRange(post, image, comment);
            setupContext.SetMorphReference(image, nameof(Image.Imageable), post);
            setupContext.SetMorphReference(comment, nameof(Comment.Commentable), post);
            await setupContext.SaveChangesAsync();
        }

        await using (var deleteContext = CreateContext(databaseName))
        {
            var post = await deleteContext.Posts.SingleAsync();
            deleteContext.Remove(post);
            await deleteContext.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);

        Assert.Empty(await assertContext.Posts.ToListAsync());
        Assert.Empty(await assertContext.Images.ToListAsync());
        Assert.Empty(await assertContext.Comments.ToListAsync());
    }

    [Fact]
    public async Task AttachMorphToMany_creates_pivot_rows_and_loads_related_models()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 11, Title = "Post" };
        var video = new Video { Id = 12, Title = "Video" };
        var firstTag = new Tag { Id = 301, Name = "news" };
        var secondTag = new Tag { Id = 302, Name = "featured" };

        dbContext.AddRange(post, video, firstTag, secondTag);
        dbContext.AttachMorphToMany<Post, Tag, TagAssignment>(post, nameof(Post.Tags), firstTag);
        dbContext.AttachMorphToMany<Post, Tag, TagAssignment>(post, nameof(Post.Tags), secondTag);
        dbContext.AttachMorphToMany<Video, Tag, TagAssignment>(video, nameof(Video.Tags), firstTag);
        await dbContext.SaveChangesAsync();

        Assert.Equal(3, await dbContext.TagAssignments.CountAsync());

        var tags = await dbContext.LoadMorphToManyAsync<Post, Tag>(post, nameof(Post.Tags));

        Assert.Equal(2, tags.Count);
        Assert.Contains(tags, tag => tag.Id == firstTag.Id);
        Assert.Contains(tags, tag => tag.Id == secondTag.Id);
        Assert.Equal(2, post.Tags.Count);
    }

    [Fact]
    public async Task LoadMorphedByMany_returns_only_owners_for_the_selected_related_model()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 21, Title = "Post" };
        var video = new Video { Id = 22, Title = "Video" };
        var tag = new Tag { Id = 401, Name = "shared" };

        dbContext.AddRange(post, video, tag);
        dbContext.AttachMorphToMany<Post, Tag, TagAssignment>(post, nameof(Post.Tags), tag);
        dbContext.AttachMorphToMany<Video, Tag, TagAssignment>(video, nameof(Video.Tags), tag);
        await dbContext.SaveChangesAsync();

        var posts = await dbContext.LoadMorphedByManyAsync<Tag, Post>(tag, nameof(Tag.Posts));

        Assert.Single(posts);
        Assert.Equal(post.Id, posts[0].Id);
        Assert.Single(tag.Posts);
    }

    [Fact]
    public async Task Deleting_owner_removes_only_its_polymorphic_pivot_rows()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setupContext = CreateContext(databaseName))
        {
            var post = new Post { Id = 31, Title = "Post" };
            var video = new Video { Id = 32, Title = "Video" };
            var tag = new Tag { Id = 501, Name = "shared" };

            setupContext.AddRange(post, video, tag);
            setupContext.AttachMorphToMany<Post, Tag, TagAssignment>(post, nameof(Post.Tags), tag);
            setupContext.AttachMorphToMany<Video, Tag, TagAssignment>(video, nameof(Video.Tags), tag);
            await setupContext.SaveChangesAsync();
        }

        await using (var deleteContext = CreateContext(databaseName))
        {
            var post = await deleteContext.Posts.SingleAsync();
            deleteContext.Remove(post);
            await deleteContext.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        Assert.Single(await assertContext.Tags.ToListAsync());
        Assert.Single(await assertContext.TagAssignments.ToListAsync());
        Assert.Equal("videos", (await assertContext.TagAssignments.SingleAsync()).TaggableType);
    }

    [Fact]
    public async Task Deleting_related_entity_removes_polymorphic_pivot_rows_in_code()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var setupContext = CreateContext(databaseName))
        {
            var post = new Post { Id = 41, Title = "Post" };
            var tag = new Tag { Id = 601, Name = "cleanup" };

            setupContext.AddRange(post, tag);
            setupContext.AttachMorphToMany<Post, Tag, TagAssignment>(post, nameof(Post.Tags), tag);
            await setupContext.SaveChangesAsync();
        }

        await using (var deleteContext = CreateContext(databaseName))
        {
            var tag = await deleteContext.Tags.SingleAsync();
            deleteContext.Remove(tag);
            await deleteContext.SaveChangesAsync();
        }

        await using var assertContext = CreateContext(databaseName);
        Assert.Single(await assertContext.Posts.ToListAsync());
        Assert.Empty(await assertContext.TagAssignments.ToListAsync());
    }

    [Fact]
    public async Task LoadMorphsAsync_batches_mixed_morph_owners()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 51, Title = "Post" };
        var video = new Video { Id = 52, Title = "Video" };
        var firstComment = new Comment { Id = 701, Body = "One" };
        var secondComment = new Comment { Id = 702, Body = "Two" };

        dbContext.AddRange(post, video, firstComment, secondComment);
        dbContext.SetMorphReference(firstComment, nameof(Comment.Commentable), post);
        dbContext.SetMorphReference(secondComment, nameof(Comment.Commentable), video);
        await dbContext.SaveChangesAsync();

        var owners = await dbContext.LoadMorphsAsync(new[] { firstComment, secondComment }, nameof(Comment.Commentable));

        Assert.Same(post, owners[firstComment]);
        Assert.Same(video, owners[secondComment]);
        Assert.Same(post, firstComment.Commentable);
        Assert.Same(video, secondComment.Commentable);
    }

    [Fact]
    public async Task LoadMorphLatestOfManyAsync_returns_latest_related_model()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 61, Title = "Post" };
        var firstComment = new Comment { Id = 801, Body = "First" };
        var secondComment = new Comment { Id = 802, Body = "Second" };

        dbContext.AddRange(post, firstComment, secondComment);
        dbContext.SetMorphReference(firstComment, nameof(Comment.Commentable), post);
        dbContext.SetMorphReference(secondComment, nameof(Comment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var latest = await dbContext.LoadMorphLatestOfManyAsync<Post, Comment, int>(
            post,
            nameof(Post.Comments),
            comment => comment.Id,
            nameof(Post.LatestComment));

        Assert.NotNull(latest);
        Assert.Equal(secondComment.Id, latest!.Id);
        Assert.Same(latest, post.LatestComment);
        Assert.Empty(post.Comments);
    }

    [Fact]
    public async Task LoadMorphLatestOfManyAsync_batches_without_hydrating_full_collections()
    {
        await using var dbContext = CreateContext();
        var firstPost = new Post { Id = 62, Title = "First" };
        var secondPost = new Post { Id = 63, Title = "Second" };
        var firstOlder = new Comment { Id = 811, Body = "Older" };
        var firstNewer = new Comment { Id = 812, Body = "Newer" };
        var secondOnly = new Comment { Id = 813, Body = "Only" };

        dbContext.AddRange(firstPost, secondPost, firstOlder, firstNewer, secondOnly);
        dbContext.SetMorphReference(firstOlder, nameof(Comment.Commentable), firstPost);
        dbContext.SetMorphReference(firstNewer, nameof(Comment.Commentable), firstPost);
        dbContext.SetMorphReference(secondOnly, nameof(Comment.Commentable), secondPost);
        await dbContext.SaveChangesAsync();

        var latestByPost = await dbContext.LoadMorphLatestOfManyAsync<Post, Comment, int>(
            new[] { firstPost, secondPost },
            nameof(Post.Comments),
            comment => comment.Id,
            nameof(Post.LatestComment));

        Assert.Equal(firstNewer.Id, latestByPost[firstPost]!.Id);
        Assert.Equal(secondOnly.Id, latestByPost[secondPost]!.Id);
        Assert.Same(firstNewer, firstPost.LatestComment);
        Assert.Same(secondOnly, secondPost.LatestComment);
        Assert.Empty(firstPost.Comments);
        Assert.Empty(secondPost.Comments);
    }

    [Fact]
    public async Task LoadMorphManyAsync_batches_inverse_relationships_for_multiple_owners()
    {
        await using var dbContext = CreateContext();
        var firstPost = new Post { Id = 71, Title = "First" };
        var secondPost = new Post { Id = 72, Title = "Second" };
        var firstComment = new Comment { Id = 901, Body = "First comment" };
        var secondComment = new Comment { Id = 902, Body = "Second comment" };

        dbContext.AddRange(firstPost, secondPost, firstComment, secondComment);
        dbContext.SetMorphReference(firstComment, nameof(Comment.Commentable), firstPost);
        dbContext.SetMorphReference(secondComment, nameof(Comment.Commentable), secondPost);
        await dbContext.SaveChangesAsync();

        var commentsByPost = await dbContext.LoadMorphManyAsync<Post, Comment>(new[] { firstPost, secondPost }, nameof(Post.Comments));

        Assert.Single(commentsByPost[firstPost]);
        Assert.Single(commentsByPost[secondPost]);
        Assert.Equal(firstComment.Id, commentsByPost[firstPost][0].Id);
        Assert.Equal(secondComment.Id, commentsByPost[secondPost][0].Id);
    }

    [Fact]
    public async Task Convention_helpers_use_laravel_style_shadow_column_names()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 81, Title = "Post" };
        var tag = new Tag { Id = 1001, Name = "laravel" };
        var conventionComment = new ConventionComment { Id = 1002, Body = "Shadow props" };

        dbContext.AddRange(post, tag, conventionComment);
        dbContext.SetMorphReference(conventionComment, nameof(ConventionComment.Commentable), post);
        dbContext.AttachMorphToMany<Post, Tag, ConventionTaggable>(post, nameof(Post.TagsByConvention), tag);
        await dbContext.SaveChangesAsync();

        Assert.Equal("posts", dbContext.Entry(conventionComment).Property<string?>("commentable_type").CurrentValue);
        Assert.Equal(81, dbContext.Entry(conventionComment).Property<int>("commentable_id").CurrentValue);

        var pivot = await dbContext.ConventionTaggables.SingleAsync();
        Assert.Equal("posts", dbContext.Entry(pivot).Property<string?>("taggable_type").CurrentValue);
        Assert.Equal(81, dbContext.Entry(pivot).Property<int>("taggable_id").CurrentValue);
        Assert.Equal(1001, dbContext.Entry(pivot).Property<int>("tag_id").CurrentValue);

        var owner = await dbContext.LoadMorphAsync<ConventionComment, Post>(conventionComment, nameof(ConventionComment.Commentable));
        var tags = await dbContext.LoadMorphToManyAsync<Post, Tag>(post, nameof(Post.TagsByConvention));

        Assert.NotNull(owner);
        Assert.Single(tags);
        Assert.Equal(tag.Id, tags[0].Id);
    }

    private static TestDbContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .UsePolymorphicRelationships()
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts => Set<Post>();

        public DbSet<Video> Videos => Set<Video>();

        public DbSet<Comment> Comments => Set<Comment>();

        public DbSet<Image> Images => Set<Image>();

        public DbSet<Tag> Tags => Set<Tag>();

        public DbSet<TagAssignment> TagAssignments => Set<TagAssignment>();

        public DbSet<ConventionComment> ConventionComments => Set<ConventionComment>();

        public DbSet<ConventionTaggable> ConventionTaggables => Set<ConventionTaggable>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<Post>("posts");
                polymorphic.MorphMap<Video>("videos");

                polymorphic.Entity<Comment>()
                    .MorphTo(nameof(Comment.Commentable), entity => entity.CommentableType, entity => entity.CommentableId)
                    .MorphMany<Post>(nameof(Post.Comments))
                    .MorphMany<Video>(nameof(Video.Comments));

                polymorphic.Entity<Image>()
                    .MorphTo(nameof(Image.Imageable), entity => entity.ImageableType, entity => entity.ImageableId)
                    .MorphOne<Post>(nameof(Post.Image))
                    .MorphOne<Video>(nameof(Video.Image));

                polymorphic.MorphToMany<Post, Tag, TagAssignment, int, int>(
                    nameof(Post.Tags),
                    nameof(Tag.Posts),
                    entity => entity.TaggableType,
                    entity => entity.TaggableId,
                    entity => entity.TagId);

                polymorphic.MorphedByMany<Tag, Video, TagAssignment, int, int>(
                    nameof(Tag.Videos),
                    nameof(Video.Tags),
                    entity => entity.TaggableType,
                    entity => entity.TaggableId,
                    entity => entity.TagId);

                polymorphic.Entity<ConventionComment>()
                    .MorphToConvention<int>(nameof(ConventionComment.Commentable))
                    .MorphMany<Post>(nameof(Post.ConventionComments));

                polymorphic.MorphToManyConvention<Post, Tag, ConventionTaggable, int, int>(
                    nameof(Post.TagsByConvention),
                    nameof(Tag.PostsByConvention),
                    "taggable");
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class Post
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        [NotMapped]
        public object? Image { get; set; }

        [NotMapped]
        public List<Comment> Comments { get; set; } = new();

        [NotMapped]
        public Comment? LatestComment { get; set; }

        [NotMapped]
        public List<ConventionComment> ConventionComments { get; set; } = new();

        [NotMapped]
        public List<Tag> Tags { get; set; } = new();

        [NotMapped]
        public List<Tag> TagsByConvention { get; set; } = new();
    }

    private sealed class Video
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        [NotMapped]
        public object? Image { get; set; }

        [NotMapped]
        public List<Comment> Comments { get; set; } = new();

        [NotMapped]
        public List<Tag> Tags { get; set; } = new();
    }

    private sealed class Comment
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? CommentableType { get; set; }

        public int? CommentableId { get; set; }

        [NotMapped]
        public object? Commentable { get; set; }
    }

    private sealed class Image
    {
        public int Id { get; set; }

        public string Url { get; set; } = string.Empty;

        public string? ImageableType { get; set; }

        public int? ImageableId { get; set; }

        [NotMapped]
        public object? Imageable { get; set; }
    }

    private sealed class Tag
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [NotMapped]
        public List<Post> Posts { get; set; } = new();

        [NotMapped]
        public List<Video> Videos { get; set; } = new();

        [NotMapped]
        public List<Post> PostsByConvention { get; set; } = new();
    }

    private sealed class TagAssignment
    {
        public int Id { get; set; }

        public string? TaggableType { get; set; }

        public int TaggableId { get; set; }

        public int TagId { get; set; }
    }

    private sealed class ConventionComment
    {
        public int Id { get; set; }

        public string Body { get; set; } = string.Empty;

        [NotMapped]
        public object? Commentable { get; set; }
    }

    private sealed class ConventionTaggable
    {
        public int Id { get; set; }
    }
}

