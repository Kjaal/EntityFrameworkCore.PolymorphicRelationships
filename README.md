# EntityFrameworkCore.PolymorphicRelationships

`EntityFrameworkCore.PolymorphicRelationships` adds Laravel-style polymorphic relationships to Entity Framework Core without requiring database-enforced foreign keys.

## Overview

- `morphTo` relationships backed by `<name>_type` and `<name>_id`
- inverse `morphOne` and `morphMany` relationships
- polymorphic many-to-many relationships with explicit pivot entities
- Laravel-style naming conventions for morph columns and pivot columns
- attribute-based relationship discovery
- runtime helpers for assigning, loading, and batch-loading morph relationships
- code-driven cascade delete for registered morph relationships
- save-time integrity validation for morph keys and pivot references
- migration/scaffolding support for designer helpers and attribute conventions

## Installation

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
```

## Runtime APIs

### Assign a morph owner

```csharp
var post = await dbContext.Posts.FindAsync(1);
var comment = new Comment { Body = "First" };

dbContext.SetMorphReference(comment, nameof(Comment.Commentable), post!);
dbContext.Comments.Add(comment);
await dbContext.SaveChangesAsync();
```

### Load a morph owner

```csharp
var owner = await dbContext.LoadMorphAsync(comment, nameof(Comment.Commentable));
var typedOwner = await dbContext.LoadMorphAsync<Comment, Post>(
    comment,
    nameof(Comment.Commentable),
    query => query.Include(post => post.Author));
```

### Batch-load mixed morph owners

```csharp
var owners = await dbContext.LoadMorphsAsync(comments, nameof(Comment.Commentable));

var ownersWithPlans = await dbContext.LoadMorphsAsync(
    comments,
    nameof(Comment.Commentable),
    plan => plan
        .For<Post>(query => query.Include(post => post.Author))
        .For<Video>(query => query.Include(video => video.Channel)));
```

### Load inverse one-to-many morphs

```csharp
var comments = await dbContext.LoadMorphManyAsync<Post, Comment>(post, nameof(Post.Comments));

var latestComment = await dbContext.LoadMorphLatestOfManyAsync<Post, Comment, int>(
    post,
    nameof(Post.Comments),
    comment => comment.Id,
    assignToPropertyName: nameof(Post.LatestComment));
```

### Load polymorphic many-to-many relationships

```csharp
var tag = new Tag { Name = "featured" };
dbContext.Tags.Add(tag);

dbContext.AttachMorphToMany<Post, Tag, Taggable>(post, nameof(Post.Tags), tag);
await dbContext.SaveChangesAsync();

var tags = await dbContext.LoadMorphToManyAsync<Post, Tag>(post, nameof(Post.Tags));
var posts = await dbContext.LoadMorphedByManyAsync<Tag, Post>(tag, nameof(Tag.Posts));
```

## Attribute-based configuration

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

## Current capabilities

- single-column owner keys
- convention-based and explicit morph registration
- `morphTo`, `morphOne`, `morphMany`, `morphToMany`, and `morphedByMany`
- one-of-many helpers with ordered EF queries
- query-transform overloads for eager loading typed relationships
- mixed-owner batch loading with per-type plans
- code-executed cascade delete interception
- save-time morph integrity validation
- migration snapshot and scaffolding compatibility for supported helpers

## Current limitations

- no database-level foreign key enforcement
- many-to-many relationships require an explicit pivot entity
- composite owner keys are not supported
- one-of-many selection is limited to a single ordering expression
- Laravel features such as `morphToMany` custom pivot behavior and broader relationship macros are not yet fully mirrored

## Roadmap

1. Configurable validation levels for existence checks and save-time enforcement
2. Richer eager-loading plans for nested `morphToMany` and `morphedByMany` batches
3. Expanded Laravel parity around custom pivot behavior and relationship ergonomics

## Packaging

```bash
dotnet pack src/EntityFrameworkCore.PolymorphicRelationships/EntityFrameworkCore.PolymorphicRelationships.csproj -c Release
```
