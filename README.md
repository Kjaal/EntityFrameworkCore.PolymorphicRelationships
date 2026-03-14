# EF Core Polymorphic Extension

This repository now has a starter package for Laravel-style polymorphic relationships on top of Entity Framework Core, without database-level foreign keys.

## What is in place

- `morphMap`-style aliases via `MorphMap<TEntity>(alias)`
- `morphTo` registration via `MorphTo(...)`
- Laravel-style shadow-column conventions via `MorphToConvention(...)`
- inverse `morphOne` and `morphMany` registration via `MorphOne<TPrincipal>(...)` and `MorphMany<TPrincipal>(...)`
- polymorphic many-to-many registration via `MorphToMany(...)` and `MorphedByMany(...)`
- Laravel-style many-to-many conventions via `MorphToManyConvention(...)` and `MorphedByManyConvention(...)`
- code-driven cascade deletion through `PolymorphicCascadeDeleteInterceptor`
- runtime helpers for assigning and loading relationships:
  - `SetMorphReference(...)`
  - `LoadMorphAsync(...)`
  - `LoadMorphsAsync(...)`
  - `LoadMorphOneAsync(...)`
  - `LoadMorphManyAsync(...)`
  - `LoadMorphLatestOfManyAsync(...)`
  - `LoadMorphOldestOfManyAsync(...)`
  - `LoadMorphOneOfManyAsync(...)`
  - `AttachMorphToMany(...)`
  - `DetachMorphToManyAsync(...)`
  - `LoadMorphToManyAsync(...)`
  - `LoadMorphedByManyAsync(...)`

## Example

```csharp
using EFCorePolymorphicExtension;
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
```

## Current boundaries

- This is a starter implementation, not full Laravel parity yet.
- This version assumes the polymorphic owner key is a single column.
- The safest first use is with assigned keys, such as GUIDs or known integer keys, before calling `SetMorphReference(...)`.
- Many-to-many currently uses an explicit pivot entity you define in EF Core.
- One-of-many helpers now order in translated EF Core queries instead of loading the full morph-many collection first.
- Batch eager-loading is available for `morphTo`, `morphMany`, `morphToMany`, and `morphedByMany`, with the mixed-owner batching focused on `morphTo`.
- Cascade delete is handled in code during `SaveChanges` or `SaveChangesAsync`, not by the database; for many-to-many that means pivot rows are cleaned up in code.

## Packaging

- The library project is packable and now includes NuGet metadata, XML docs, symbols, and the root `README.md` in the package.
- Create a package with `dotnet pack src/EFCorePolymorphicExtension/EFCorePolymorphicExtension.csproj -c Release`.

## Near-term next steps

1. Add richer eager-loading helpers with per-type constraints for `morphTo` batches.
2. Add integrity validation beyond the current naming conventions.
3. Add more Laravel parity helpers around custom pivot behavior and relationship ergonomics.

