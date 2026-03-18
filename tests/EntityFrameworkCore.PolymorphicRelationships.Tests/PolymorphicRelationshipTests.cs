using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace EntityFrameworkCore.PolymorphicRelationships.Tests;

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
    public async Task SavingChanges_syncs_morph_many_from_principal_collection()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 3, Title = "Tracked" };
        post.Comments.Add(new Comment { Id = 103, Body = "Created from collection" });

        dbContext.Add(post);
        await dbContext.SaveChangesAsync();

        var comment = await dbContext.Comments.SingleAsync();
        Assert.Equal("posts", comment.CommentableType);
        Assert.Equal(post.Id, comment.CommentableId);
    }

    [Fact]
    public async Task SavingChanges_syncs_morph_to_from_navigation_assignment()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 5, Title = "Owner" };
        var comment = new Comment { Id = 104, Body = "Created from owner nav", Commentable = post };

        dbContext.AddRange(post, comment);
        await dbContext.SaveChangesAsync();

        Assert.Equal("posts", comment.CommentableType);
        Assert.Equal(post.Id, comment.CommentableId);
    }

    [Fact]
    public async Task SavingChanges_clears_morph_columns_when_navigation_is_cleared()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 6, Title = "Owner" };
        var comment = new Comment { Id = 105, Body = "Owned", Commentable = post };

        dbContext.AddRange(post, comment);
        await dbContext.SaveChangesAsync();

        comment.Commentable = null;
        await dbContext.SaveChangesAsync();

        Assert.Null(comment.CommentableType);
        Assert.Null(comment.CommentableId);
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
    public async Task SavingChanges_syncs_morph_to_many_from_collection_navigation()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 16, Title = "Post" };
        var tag = new Tag { Id = 303, Name = "tag" };
        post.Tags.Add(tag);

        dbContext.Add(post);
        dbContext.Add(tag);
        await dbContext.SaveChangesAsync();

        var pivot = await dbContext.TagAssignments.SingleAsync();
        Assert.Equal("posts", pivot.TaggableType);
        Assert.Equal(post.Id, pivot.TaggableId);
        Assert.Equal(tag.Id, pivot.TagId);
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
    public async Task IncludeMorph_loads_inverse_collection_like_include()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 58, Title = "Post" };
        var comment = new Comment { Id = 703, Body = "Included" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(Comment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var includedPost = await dbContext.Posts
            .IncludeMorph(entity => entity.Comments)
            .Where(entity => entity.Id == post.Id)
            .SingleAsync();

        Assert.Single(includedPost.Comments);
        Assert.Equal(comment.Id, includedPost.Comments[0].Id);
    }

    [Fact]
    public async Task IncludeMorph_loads_morph_owner_like_include()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 59, Title = "Post" };
        var comment = new Comment { Id = 704, Body = "Included owner" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(Comment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var includedComment = await dbContext.Comments
            .IncludeMorph(entity => entity.Commentable)
            .Where(entity => entity.Id == comment.Id)
            .SingleAsync();

        Assert.NotNull(includedComment.Commentable);
        Assert.IsType<Post>(includedComment.Commentable);
    }

    [Fact]
    public async Task IncludeMorph_loads_many_to_many_like_include()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 60, Title = "Post" };
        var tag = new Tag { Id = 402, Name = "Tag" };

        dbContext.AddRange(post, tag);
        dbContext.AttachMorphToMany<Post, Tag, TagAssignment>(post, nameof(Post.Tags), tag);
        await dbContext.SaveChangesAsync();

        var includedPost = await dbContext.Posts
            .IncludeMorph(entity => entity.Tags)
            .Where(entity => entity.Id == post.Id)
            .SingleAsync();

        Assert.Single(includedPost.Tags);
        Assert.Equal(tag.Id, includedPost.Tags[0].Id);
    }

    [Fact]
    public async Task Native_select_projects_polymorphic_collection_like_include_results()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 61, Title = "Post" };
        var firstComment = new Comment { Id = 705, Body = "First" };
        var secondComment = new Comment { Id = 706, Body = "Second" };

        dbContext.AddRange(post, firstComment, secondComment);
        dbContext.SetMorphReference(firstComment, nameof(Comment.Commentable), post);
        dbContext.SetMorphReference(secondComment, nameof(Comment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var selected = await dbContext.Posts
            .Where(entity => entity.Id == post.Id)
            .Select(entity => new PostProjection
            {
                Title = entity.Title,
                Comments = entity.Comments,
            })
            .SingleAsync();

        var included = await dbContext.Posts
            .IncludeMorph(entity => entity.Comments)
            .Where(entity => entity.Id == post.Id)
            .SingleAsync();

        Assert.Equal(included.Title, selected.Title);
        Assert.Equal(included.Comments.Select(entity => entity.Id).OrderBy(entity => entity), selected.Comments.Select(entity => entity.Id).OrderBy(entity => entity));
    }

    [Fact]
    public async Task Native_select_projects_polymorphic_owner_like_include_results()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 62, Title = "Post" };
        var comment = new Comment { Id = 707, Body = "Owner" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(Comment.Commentable), post);
        await dbContext.SaveChangesAsync();

        var selected = await dbContext.Comments
            .Where(entity => entity.Id == comment.Id)
            .Select(entity => new CommentProjection
            {
                Body = entity.Body,
                Commentable = entity.Commentable,
            })
            .SingleAsync();

        var included = await dbContext.Comments
            .IncludeMorph(entity => entity.Commentable)
            .Where(entity => entity.Id == comment.Id)
            .SingleAsync();

        Assert.Equal(included.Body, selected.Body);
        Assert.IsType<Post>(selected.Commentable);
        Assert.Equal(((Post)included.Commentable!).Id, ((Post)selected.Commentable!).Id);
    }

    [Fact]
    public async Task Native_orderby_supports_casted_polymorphic_owner_property_on_relational_provider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var zPost = new Post { Id = 63, Title = "Zulu" };
        var aPost = new Post { Id = 64, Title = "Alpha" };
        var firstComment = new Comment { Id = 708, Body = "Zulu comment" };
        var secondComment = new Comment { Id = 709, Body = "Alpha comment" };

        dbContext.AddRange(zPost, aPost, firstComment, secondComment);
        dbContext.SetMorphReference(firstComment, nameof(Comment.Commentable), zPost);
        dbContext.SetMorphReference(secondComment, nameof(Comment.Commentable), aPost);
        await dbContext.SaveChangesAsync();

        var orderedBodies = await dbContext.Comments
            .OrderBy(entity => ((Post)entity.Commentable!).Title)
            .Select(entity => entity.Body)
            .ToListAsync();

        Assert.Equal(new[] { secondComment.Body, firstComment.Body }, orderedBodies);
    }

    [Fact]
    public async Task Native_where_supports_polymorphic_collection_count_on_relational_provider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var firstPost = new Post { Id = 67, Title = "First" };
        var secondPost = new Post { Id = 68, Title = "Second" };
        var firstComment = new Comment { Id = 712, Body = "One" };
        var secondComment = new Comment { Id = 713, Body = "Two" };

        dbContext.AddRange(firstPost, secondPost, firstComment, secondComment);
        dbContext.SetMorphReference(firstComment, nameof(Comment.Commentable), firstPost);
        dbContext.SetMorphReference(secondComment, nameof(Comment.Commentable), firstPost);
        await dbContext.SaveChangesAsync();

        var posts = await dbContext.Posts
            .Where(entity => entity.Comments.Count > 0)
            .ToListAsync();

        Assert.Single(posts);
        Assert.Equal(firstPost.Id, posts[0].Id);
    }

    [Fact]
    public async Task Native_where_supports_polymorphic_collection_count_comparisons_beyond_zero_on_relational_provider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var firstPost = new Post { Id = 71, Title = "First" };
        var secondPost = new Post { Id = 72, Title = "Second" };
        var thirdPost = new Post { Id = 73, Title = "Third" };
        var firstComment = new Comment { Id = 715, Body = "One" };
        var secondComment = new Comment { Id = 716, Body = "Two" };

        dbContext.AddRange(firstPost, secondPost, thirdPost, firstComment, secondComment);
        dbContext.SetMorphReference(firstComment, nameof(Comment.Commentable), firstPost);
        dbContext.SetMorphReference(secondComment, nameof(Comment.Commentable), firstPost);
        await dbContext.SaveChangesAsync();

        var gtOne = await dbContext.Posts.Where(entity => entity.Comments.Count > 1).Select(entity => entity.Id).ToListAsync();
        var eqZero = await dbContext.Posts.Where(entity => entity.Comments.Count == 0).Select(entity => entity.Id).OrderBy(entity => entity).ToListAsync();
        var neqZero = await dbContext.Posts.Where(entity => entity.Comments.Count != 0).Select(entity => entity.Id).ToListAsync();

        Assert.Equal(new[] { firstPost.Id }, gtOne);
        Assert.Equal(new[] { secondPost.Id, thirdPost.Id }, eqZero);
        Assert.Equal(new[] { firstPost.Id }, neqZero);
    }

    [Fact]
    public async Task Native_where_supports_polymorphic_collection_any_on_relational_provider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateSqliteContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var firstPost = new Post { Id = 69, Title = "First" };
        var secondPost = new Post { Id = 70, Title = "Second" };
        var comment = new Comment { Id = 714, Body = "One" };

        dbContext.AddRange(firstPost, secondPost, comment);
        dbContext.SetMorphReference(comment, nameof(Comment.Commentable), firstPost);
        await dbContext.SaveChangesAsync();

        var posts = await dbContext.Posts
            .Where(entity => entity.Comments.Any())
            .ToListAsync();

        Assert.Single(posts);
        Assert.Equal(firstPost.Id, posts[0].Id);
    }

    [Fact]
    public async Task Guid_primary_keys_work_for_morph_relationships()
    {
        await using var dbContext = CreateGuidContext();
        var postId = Guid.NewGuid();
        var commentId = Guid.NewGuid();
        var post = new GuidPost { Id = postId, Title = "Guid owner" };
        var comment = new GuidComment { Id = commentId, Body = "Guid child" };

        dbContext.AddRange(post, comment);
        dbContext.SetMorphReference(comment, nameof(GuidComment.Commentable), post);
        await dbContext.SaveChangesAsync();

        Assert.Equal("guid_posts", comment.CommentableType);
        Assert.Equal(postId, comment.CommentableId);

        var owner = await dbContext.LoadMorphAsync<GuidComment, GuidPost>(comment, nameof(GuidComment.Commentable));
        Assert.NotNull(owner);
        Assert.Equal(postId, owner!.Id);
    }

    [Fact]
    public async Task Duplicate_morph_aliases_are_rejected()
    {
        var options = new DbContextOptionsBuilder<DuplicateAliasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UsePolymorphicRelationships()
            .Options;

        await using var dbContext = new DuplicateAliasDbContext(options);
        var exception = Assert.Throws<InvalidOperationException>(() => dbContext.Model);
        Assert.Contains("Morph alias 'shared' is already registered", exception.Message);
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
    public async Task LoadMorphManyAcrossAsync_batches_inverse_relationships_for_mixed_principals()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 73, Title = "Post" };
        var video = new Video { Id = 74, Title = "Video" };
        var postComment = new Comment { Id = 903, Body = "Post comment" };
        var videoComment = new Comment { Id = 904, Body = "Video comment" };

        dbContext.AddRange(post, video, postComment, videoComment);
        dbContext.SetMorphReference(postComment, nameof(Comment.Commentable), post);
        dbContext.SetMorphReference(videoComment, nameof(Comment.Commentable), video);
        await dbContext.SaveChangesAsync();

        var commentsByPrincipal = await dbContext.LoadMorphManyAcrossAsync<Comment>(new object[] { post, video }, nameof(Post.Comments));

        Assert.Single(commentsByPrincipal[post]);
        Assert.Single(commentsByPrincipal[video]);
        Assert.Equal(postComment.Id, commentsByPrincipal[post][0].Id);
        Assert.Equal(videoComment.Id, commentsByPrincipal[video][0].Id);
    }

    [Fact]
    public async Task Untracked_helpers_load_expected_relationships()
    {
        await using var dbContext = CreateContext();
        var post = new Post { Id = 75, Title = "Post" };
        var comment = new Comment { Id = 905, Body = "Comment" };
        var tag = new Tag { Id = 1003, Name = "Tag" };

        dbContext.AddRange(post, comment, tag);
        dbContext.SetMorphReference(comment, nameof(Comment.Commentable), post);
        dbContext.AttachMorphToMany<Post, Tag, TagAssignment>(post, nameof(Post.Tags), tag);
        await dbContext.SaveChangesAsync();

        var owner = await dbContext.LoadMorphUntrackedAsync<Comment, Post>(comment, nameof(Comment.Commentable));
        var comments = await dbContext.LoadMorphManyUntrackedAsync<Post, Comment>(new[] { post }, nameof(Post.Comments));
        var tags = await dbContext.LoadMorphToManyUntrackedAsync<Post, Tag>(new[] { post }, nameof(Post.Tags));

        Assert.NotNull(owner);
        Assert.Single(comments[post]);
        Assert.Single(tags[post]);
        Assert.Equal(tag.Id, tags[post][0].Id);
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

    private static GuidTestDbContext CreateGuidContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<GuidTestDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .UsePolymorphicRelationships()
            .Options;

        return new GuidTestDbContext(options);
    }

    private static TestDbContext CreateSqliteContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
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

    private sealed class GuidTestDbContext(DbContextOptions<GuidTestDbContext> options) : DbContext(options)
    {
        public DbSet<GuidPost> Posts => Set<GuidPost>();

        public DbSet<GuidComment> Comments => Set<GuidComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<GuidPost>("guid_posts");

                polymorphic.Entity<GuidComment>()
                    .MorphTo<Guid?>(nameof(GuidComment.Commentable), entity => entity.CommentableType, entity => entity.CommentableId)
                    .MorphMany<GuidPost>(nameof(GuidPost.Comments));
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class DuplicateAliasDbContext(DbContextOptions<DuplicateAliasDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UsePolymorphicRelationships(polymorphic =>
            {
                polymorphic.MorphMap<Post>("shared");
                polymorphic.MorphMap<Video>("shared");
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

    private sealed class PostProjection
    {
        public string Title { get; set; } = string.Empty;

        public List<Comment> Comments { get; set; } = new();
    }

    private sealed class CommentProjection
    {
        public string Body { get; set; } = string.Empty;

        public object? Commentable { get; set; }
    }

    private sealed class GuidPost
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;

        [NotMapped]
        public List<GuidComment> Comments { get; set; } = new();
    }

    private sealed class GuidComment
    {
        public Guid Id { get; set; }

        public string Body { get; set; } = string.Empty;

        public string? CommentableType { get; set; }

        public Guid? CommentableId { get; set; }

        [NotMapped]
        public object? Commentable { get; set; }
    }
}


