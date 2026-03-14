# Entity Framework Core Polymorphic Relationships

This repository now has a starter package for Laravel-style polymorphic relationships on top of Entity Framework Core, without database-level foreign keys.

## What is in place

- `morphMap`-style aliases via `MorphMap<TEntity>(alias)`
- `morphTo` registration via `MorphTo(...)`
- Laravel-style shadow-column conventions via `MorphToConvention(...)`
- attribute-driven model discovery via `UsePolymorphicRelationshipAttributes()`
- designer helpers via `HasMorphColumns(...)` and `HasMorphToManyColumns(...)`
- inverse `morphOne` and `morphMany` registration via `MorphOne<TPrincipal>(...)` and `MorphMany<TPrincipal>(...)`
- polymorphic many-to-many registration via `MorphToMany(...)` and `MorphedByMany(...)`
- Laravel-style many-to-many conventions via `MorphToManyConvention(...)` and `MorphedByManyConvention(...)`
- code-driven cascade deletion through `PolymorphicCascadeDeleteInterceptor`
- runtime helpers for assigning and loading relationships:
  - `SetMorphReference(...)`
  - `LoadMorphAsync(...)`
  - `LoadMorphsAsync(...)`
  - per-type eager-loading plans for mixed `morphTo` batches via `LoadMorphsAsync(..., plan => ...)`
  - `LoadMorphOneAsync(...)`
  - `LoadMorphManyAsync(...)`
  - eager-loading query transforms on typed `LoadMorphAsync(...)`, `LoadMorphManyAsync(...)`, `LoadMorphToManyAsync(...)`, and `LoadMorphedByManyAsync(...)`
  - `LoadMorphLatestOfManyAsync(...)`
  - `LoadMorphOldestOfManyAsync(...)`
  - `LoadMorphOneOfManyAsync(...)`
  - `AttachMorphToMany(...)`
  - `DetachMorphToManyAsync(...)`
  - `LoadMorphToManyAsync(...)`
  - `LoadMorphedByManyAsync(...)`

## Example

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
                .MorphToConvention<int>(nameof(Comment.Commentable))
                .MorphMany<Post>(nameof(Post.Comments))
                .MorphMany<Video>(nameof(Video.Comments));

            polymorphic.Entity<Image>()
                .MorphTo(nameof(Image.Imageable), entity => entity.ImageableType, entity => entity.ImageableId)
                .MorphOne<Post>(nameof(Post.Image))
                .MorphOne<Video>(nameof(Video.Image));

            polymorphic.MorphToManyConvention<Post, Tag, Taggable, int, int>(
                nameof(Post.Tags),
                nameof(Tag.Posts),
                "taggable");

            polymorphic.MorphedByManyConvention<Tag, Video, Taggable, int, int>(
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

await using var dbContext = new AppDbContext(options);

var post = await dbContext.Posts.FindAsync(1);
var comment = new Comment { Body = "First" };

dbContext.SetMorphReference(comment, nameof(Comment.Commentable), post!);
dbContext.Comments.Add(comment);
await dbContext.SaveChangesAsync();

var owner = await dbContext.LoadMorphAsync(comment, nameof(Comment.Commentable));
var owners = await dbContext.LoadMorphsAsync(new[] { comment }, nameof(Comment.Commentable));
var ownersWithPlans = await dbContext.LoadMorphsAsync(
    new[] { comment },
    nameof(Comment.Commentable),
    plan => plan.For<Post>(query => query.Include(post => post.Author)));
var comments = await dbContext.LoadMorphManyAsync<Post, Comment>(post!, nameof(Post.Comments));
var latestComment = await dbContext.LoadMorphLatestOfManyAsync<Post, Comment, int>(
    post!,
    nameof(Post.Comments),
    entity => entity.Id,
    assignToPropertyName: nameof(Post.LatestComment));

var tag = new Tag { Name = "featured" };
dbContext.Tags.Add(tag);
dbContext.AttachMorphToMany<Post, Tag, Taggable>(post!, nameof(Post.Tags), tag);
await dbContext.SaveChangesAsync();

var tags = await dbContext.LoadMorphToManyAsync<Post, Tag>(post!, nameof(Post.Tags));
var posts = await dbContext.LoadMorphedByManyAsync<Tag, Post>(tag, nameof(Tag.Posts));

var ownerWithIncludes = await dbContext.LoadMorphAsync<Comment, Post>(
    comment,
    nameof(Comment.Commentable),
    query => query.Include(post => post.Author));
```

## Attribute conventions

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using EntityFrameworkCore.PolymorphicRelationships.Attributes;

[MorphMap("posts")]
public sealed class Post
{
    public int Id { get; set; }

    [NotMapped]
    [MorphMany(typeof(Comment), nameof(Comment.Commentable))]
    public List<Comment> Comments { get; set; } = new();

    [NotMapped]
    [MorphToMany(typeof(Tag), typeof(Taggable), nameof(Tag.Posts), "taggable")]
    public List<Tag> Tags { get; set; } = new();
}

public sealed class Comment
{
    public int Id { get; set; }
    public string? CommentableType { get; set; }
    public int? CommentableId { get; set; }

    [NotMapped]
    [MorphTo(nameof(CommentableType))]
    [ForeignKey(nameof(CommentableId))]
    public object? Commentable { get; set; }
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UsePolymorphicRelationshipAttributes();
}
```

## Designer helpers

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Comment>().HasMorphColumns<int>("commentable");
    modelBuilder.Entity<Taggable>().HasMorphToManyColumns<int, int>("taggable", typeof(Tag));
}
```

## Current boundaries

- This is a starter implementation, not full Laravel parity yet.
- This version assumes the polymorphic owner key is a single column.
- The safest first use is with assigned keys, such as GUIDs or known integer keys, before calling `SetMorphReference(...)`.
- Many-to-many currently uses an explicit pivot entity you define in EF Core.
- One-of-many helpers now order in translated EF Core queries instead of loading the full morph-many collection first.
- Batch eager-loading is available for `morphTo`, `morphMany`, `morphToMany`, and `morphedByMany`, with typed loaders supporting query transforms for `Include(...)` and filtering, and mixed `morphTo` batches supporting per-type load plans.
- Integrity validation now runs during `SaveChanges` and `SaveChangesAsync` to reject partial morph key pairs, unknown morph aliases, and references to missing owners.
- Cascade delete is handled in code during `SaveChanges` or `SaveChangesAsync`, not by the database; for many-to-many that means pivot rows are cleaned up in code.

## Packaging

- The library project is packable and now includes NuGet metadata, XML docs, symbols, and the root `README.md` in the package.
- Create a package with `dotnet pack src/EntityFrameworkCore.PolymorphicRelationships/EntityFrameworkCore.PolymorphicRelationships.csproj -c Release`.

## Near-term next steps

1. Add configurable validation levels so existence checks can be relaxed or made stricter per context.
2. Add richer eager-loading plans for nested `morphToMany` and `morphedByMany` batches.
3. Decide whether to align the package id and namespace with the GitHub repository name.


