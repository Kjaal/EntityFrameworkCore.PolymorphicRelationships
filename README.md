# EntityFrameworkCore.PolymorphicRelationships

`EntityFrameworkCore.PolymorphicRelationships` adds Laravel-style polymorphic relationships to Entity Framework Core 8 without requiring database-enforced foreign keys.

## Overview

- `morphTo` relationships backed by `<name>_type` and `<name>_id`
- inverse `morphOne` and `morphMany` relationships
- polymorphic many-to-many relationships with explicit pivot entities
- Laravel-style naming conventions for morph columns and pivot columns
- attribute-based relationship discovery
- navigation-first save behavior plus include-style polymorphic loading helpers
- temporary/store-generated key repair for new morph owners and new pivot rows
- save-time uniqueness enforcement for `morphOne` and polymorphic pivot pairs
- safe removal sync for relationships that were explicitly loaded through package APIs
- code-driven cascade delete for registered morph relationships
- save-time integrity validation for morph keys and pivot references
- migration/scaffolding support for designer helpers and attribute conventions

## Installation

Current package target:

- EF Core 8 / `.NET 8`

```bash
dotnet add package EntityFrameworkCore.PolymorphicRelationships
```

## Basic setup

```csharp
using EntityFrameworkCore.PolymorphicRelationships;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Taggable> Taggables => Set<Taggable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UsePolymorphicRelationships(polymorphic =>
        {
            polymorphic.MorphMap<Post>("posts");
            polymorphic.MorphMap<Video>("videos");

            polymorphic.Entity<Comment>()
                .MorphToConvention<Guid>(nameof(Comment.Commentable))
                .MorphMany<Post>(nameof(Post.Comments))
                .MorphMany<Video>(nameof(Video.Comments));

            polymorphic.Entity<Image>()
                .MorphTo(nameof(Image.Imageable), entity => entity.ImageableType, entity => entity.ImageableId)
                .MorphOne<Post>(nameof(Post.Image))
                .MorphOne<Video>(nameof(Video.Image));

            polymorphic.MorphToManyConvention<Post, Tag, Taggable, Guid, Guid>(
                nameof(Post.Tags),
                nameof(Tag.Posts),
                "taggable");

            polymorphic.MorphedByManyConvention<Tag, Video, Taggable, Guid, Guid>(
                nameof(Tag.Videos),
                nameof(Video.Tags),
                "taggable");
        });
    }
}

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString)
    .UsePolymorphicRelationships()
    .Options;
```

## Usage

### Persist polymorphic children through navigations

```csharp
var post = await dbContext.Posts.FindAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

post!.Comments.Add(new Comment { Body = "First" });
await dbContext.SaveChangesAsync();
```

The package synchronizes the morph type and morph id during `SaveChanges` and `SaveChangesAsync`, so adding dependents through configured inverse navigations works like a regular EF-style relationship.

Store-generated keys are also supported for newly added owners and newly added many-to-many pairs. If an owner or related entity receives its key from the database, the package repairs the final morph values after the insert completes.

Direct dependent-side navigation assignment is also supported:

```csharp
var comment = new Comment { Body = "Second", Commentable = post };
dbContext.Comments.Add(comment);
await dbContext.SaveChangesAsync();
```

Polymorphic many-to-many relationships can also be synchronized from collection navigations:

```csharp
post.Tags.Add(tag);
await dbContext.SaveChangesAsync();
```

If you remove entities from a relationship that was previously loaded through `IncludeMorph(...)` or one of the package load helpers, the package now treats that as an intentional tracked removal and clears the morph link or deletes the matching pivot row during `SaveChanges`.

### Load polymorphic relationships with include-style syntax

```csharp
var postWithComments = await dbContext.Posts
    .IncludeMorph(entity => entity.Comments)
    .Where(entity => entity.Id == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
    .SingleAsync();

var commentWithOwner = await dbContext.Comments
    .IncludeMorph(entity => entity.Commentable)
    .Where(entity => entity.Id == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"))
    .SingleAsync();

var postWithTags = await dbContext.Posts
    .IncludeMorph(entity => entity.Tags)
    .Where(entity => entity.Id == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
    .SingleAsync();

var orderedPosts = await dbContext.Posts
    .IncludeMorph(entity => entity.Comments)
    .AsNoTracking()
    .OrderByDescending(entity => entity.Id)
    .Take(20)
    .ToListAsync();

var commentWithOwnerPlan = await dbContext.Comments
    .IncludeMorph(
        entity => entity.Commentable,
        plan => plan.For<Post>(query => query.Include(post => post.Author)))
    .Where(entity => entity.Id == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"))
    .SingleAsync();
```

`IncludeMorph(...)` supports query-shaping methods such as `Where(...)`, `OrderBy(...)`, `OrderByDescending(...)`, `Skip(...)`, `Take(...)`, `AsNoTracking()`, `ToListAsync()`, `ToArrayAsync()`, `FirstAsync()`, `FirstOrDefaultAsync()`, `SingleAsync()`, `SingleOrDefaultAsync()`, and `SelectAsync(...)`.

### Project polymorphic relationships with native `Select(...)`

Experimental native `Select(...)` projection support must be enabled explicitly:

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString)
    .UsePolymorphicRelationships(options => options.EnableExperimentalSelectProjectionSupport())
    .Options;
```

```csharp
var projectedPost = await dbContext.Posts
    .Where(entity => entity.Id == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
    .Select(entity => new PostDto
    {
        Title = entity.Title,
        Comments = entity.Comments,
    })
    .SingleAsync();

var projectedComment = await dbContext.Comments
    .Where(entity => entity.Id == Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"))
    .Select(entity => new CommentDto
    {
        Body = entity.Body,
        Commentable = entity.Commentable,
    })
    .SingleAsync();
```

Native `Select(...)` projection support currently remains experimental. It works for polymorphic navigations without introducing a separate projection API, but projection-time polymorphic navigation loading is resolved after the root query is materialized and uses a companion `DbContext`. For production workloads, prefer `IncludeMorph(...)` or the lower-level load helpers unless you explicitly accept these tradeoffs.

### Native translated query support

```csharp
var postsWithComments = await dbContext.Posts
    .Where(entity => entity.Comments.Any())
    .ToListAsync();

var postsWithAtLeastOneComment = await dbContext.Posts
    .Where(entity => entity.Comments.Count > 0)
    .ToListAsync();

var orderedComments = await dbContext.Comments
    .Where(entity => entity.CommentableType == "posts")
    .OrderBy(entity => ((Post)entity.Commentable!).Title)
    .ToListAsync();

var postsWithoutComments = await dbContext.Posts
    .Where(entity => entity.Comments.Count == 0)
    .ToListAsync();

var postsWithMultipleComments = await dbContext.Posts
    .Where(entity => entity.Comments.Count > 1)
    .ToListAsync();
```

The package currently supports native translated query shapes for:

- `morphMany.Any()`
- `morphMany.Count` comparisons such as `> 0`, `> 1`, `== 0`, and `!= 0`
- owner-property ordering for `morphTo` when the owner type is explicitly cast in the query

Provider support for translated query shapes is currently exercised in CI and tests in this order:

1. PostgreSQL
2. SQLite
3. SQL Server

PostgreSQL is the primary relational target for translated polymorphic query behavior. SQLite is used for fast relational coverage in tests, and SQL Server currently has smoke/query-generation coverage. Other providers should be treated as unsupported until they have dedicated execution coverage.

Native translated query support is intentionally narrower than native `Select(...)` projection support. Unsupported shapes should continue to use `IncludeMorph(...)` or the lower-level helper APIs.

### Advanced loading helpers

```csharp
var owners = await dbContext.LoadMorphsAsync(comments, nameof(Comment.Commentable));

var ownersWithPlans = await dbContext.LoadMorphsAsync(
    comments,
    nameof(Comment.Commentable),
    plan => plan
        .For<Post>(query => query.Include(post => post.Author))
        .For<Video>(query => query.Include(video => video.Channel)));

var mixedInverse = await dbContext.LoadMorphManyAcrossAsync<Comment>(
    principals,
    nameof(Post.Comments));

var latestComment = await dbContext.LoadMorphLatestOfManyAsync<Post, Comment, int>(
    post!,
    nameof(Post.Comments),
    comment => comment.CreatedAtUtc,
    assignToPropertyName: nameof(Post.LatestComment));
```

### Untracked read helpers

```csharp
var owners = await dbContext.LoadMorphsUntrackedAsync(comments, nameof(Comment.Commentable));
var commentsByPost = await dbContext.LoadMorphManyUntrackedAsync<Post, Comment>(posts, nameof(Post.Comments));
var tagsByPost = await dbContext.LoadMorphToManyUntrackedAsync<Post, Tag>(posts, nameof(Post.Tags));
```

## Attribute-based configuration

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using EntityFrameworkCore.PolymorphicRelationships.Attributes;

[MorphMap("posts")]
public sealed class Post
{
    public Guid Id { get; set; }

    [NotMapped]
    [MorphMany(typeof(Comment), nameof(Comment.Commentable))]
    public List<Comment> Comments { get; set; } = new();

    [NotMapped]
    [MorphToMany(typeof(Tag), typeof(Taggable), nameof(Tag.Posts), "taggable")]
    public List<Tag> Tags { get; set; } = new();
}

public sealed class Comment
{
    public Guid Id { get; set; }
    public string? CommentableType { get; set; }
    public Guid? CommentableId { get; set; }

    [NotMapped]
    [MorphTo(nameof(CommentableType))]
    [ForeignKey(nameof(CommentableId))]
    public object? Commentable { get; set; }
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UsePolymorphicRelationshipAttributes();
}

var post = await dbContext.Posts.FindAsync(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
post!.Comments.Add(new Comment { Body = "Created through collection" });
await dbContext.SaveChangesAsync();
```

## Designer helpers

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Comment>().HasMorphColumns<Guid>("commentable");
    modelBuilder.Entity<Taggable>().HasMorphToManyColumns<Guid, Guid>("taggable", typeof(Tag));
}
```

## Current capabilities

- single-column owner keys, including `Guid` primary keys
- convention-based and explicit morph registration
- `morphTo`, `morphOne`, `morphMany`, `morphToMany`, and `morphedByMany`
- one-of-many helpers with ordered EF queries
- query-transform overloads for eager loading typed relationships
- mixed-owner batch loading with per-type plans
- code-executed cascade delete interception
- save-time morph integrity validation
- repair of temporary/store-generated morph keys after insert
- uniqueness enforcement for `morphOne` and polymorphic many-to-many pivot pairs
- tracked removal sync for loaded morph collections and loaded `morphOne` navigations
- migration snapshot and scaffolding compatibility for supported helpers
- translated relational query support for selected native `Where(...)` / `OrderBy(...)` polymorphic shapes
- EF Core 8 / `.NET 8` target with SQLite, PostgreSQL, and SQL Server coverage, plus CI lanes for Windows and PostgreSQL

## Current limitations

- no database-level foreign key enforcement
- many-to-many relationships require an explicit pivot entity
- composite owner keys are not supported
- one-of-many selection is limited to a single ordering expression
- translated native query support currently focuses on selected `morphMany` aggregate filters and owner-property ordering rather than the full space of polymorphic query expressions
- native translated owner-property access currently expects an explicit owner cast in the query expression
- native `Select(...)` polymorphic projection support is experimental and requires explicit opt-in
- automatic removal sync only applies to relationships that were loaded through package APIs in the current `DbContext`
- Laravel features such as `morphToMany` custom pivot behavior and broader relationship macros are not yet fully mirrored

## Roadmap

1. Expand native translated polymorphic query support beyond current `Any()` / `Count` / owner-ordering shapes
2. Deepen PostgreSQL-first provider coverage and improve SQL Server execution coverage beyond smoke tests
3. Expand Laravel parity around custom pivot behavior and relationship ergonomics

## Packaging

```bash
dotnet pack src/EntityFrameworkCore.PolymorphicRelationships/EntityFrameworkCore.PolymorphicRelationships.csproj -c Release
```
