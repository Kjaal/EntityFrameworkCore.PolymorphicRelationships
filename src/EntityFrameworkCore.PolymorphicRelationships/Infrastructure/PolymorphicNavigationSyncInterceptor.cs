using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

public sealed class PolymorphicNavigationSyncInterceptor : SaveChangesInterceptor
{
    private static readonly MethodInfo SetMorphReferenceMethod = typeof(DbContextMorphExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(DbContextMorphExtensions.SetMorphReference));

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            SyncNavigations(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            SyncNavigations(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void SyncNavigations(DbContext dbContext)
    {
        SyncMorphToAssignments(dbContext);
        SyncInverseMorphNavigations(dbContext);
    }

    private static void SyncMorphToAssignments(DbContext dbContext)
    {
        foreach (var reference in PolymorphicModelMetadata.GetReferences(dbContext.Model))
        {
            var entries = dbContext.ChangeTracker.Entries()
                .Where(entry => reference.DependentType.IsAssignableFrom(entry.Entity.GetType())
                    && entry.State != EntityState.Deleted)
                .ToList();

            foreach (var entry in entries)
            {
                var owner = PolymorphicMemberAccessorCache.GetValue(dbContext, entry.Entity, reference.RelationshipName);
                if (owner is null)
                {
                    continue;
                }

                EnsureEntityTracked(dbContext, owner, preferAttach: true);
                EnsureEntityTracked(dbContext, entry.Entity, preferAttach: false);
                SetMorphReference(dbContext, entry.Entity, reference.RelationshipName, owner);
            }
        }
    }

    private static void SyncInverseMorphNavigations(DbContext dbContext)
    {
        foreach (var reference in PolymorphicModelMetadata.GetReferences(dbContext.Model))
        {
            foreach (var association in reference.Associations)
            {
                var principals = dbContext.ChangeTracker.Entries()
                    .Where(entry => association.PrincipalType.IsAssignableFrom(entry.Entity.GetType())
                        && entry.State != EntityState.Deleted)
                    .ToList();

                foreach (var principalEntry in principals)
                {
                    if (association.Multiplicity == MorphMultiplicity.One)
                    {
                        var dependent = PolymorphicMemberAccessorCache.GetValue(dbContext, principalEntry.Entity, association.InverseRelationshipName);
                        if (dependent is null)
                        {
                            continue;
                        }

                        EnsureEntityTracked(dbContext, dependent, preferAttach: false);
                        SetMorphReference(dbContext, dependent, reference.RelationshipName, principalEntry.Entity);
                    }
                    else
                    {
                        var dependents = PolymorphicMemberAccessorCache.GetValue(dbContext, principalEntry.Entity, association.InverseRelationshipName) as System.Collections.IEnumerable;
                        if (dependents is null)
                        {
                            continue;
                        }

                        foreach (var dependent in dependents)
                        {
                            if (dependent is null)
                            {
                                continue;
                            }

                            EnsureEntityTracked(dbContext, dependent, preferAttach: false);
                            SetMorphReference(dbContext, dependent, reference.RelationshipName, principalEntry.Entity);
                        }
                    }
                }
            }
        }
    }

    private static void EnsureEntityTracked(DbContext dbContext, object entity, bool preferAttach)
    {
        var entry = dbContext.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            return;
        }

        if (preferAttach && HasNonDefaultPrimaryKey(dbContext, entity))
        {
            dbContext.Attach(entity);
        }
        else
        {
            dbContext.Add(entity);
        }
    }

    private static bool HasNonDefaultPrimaryKey(DbContext dbContext, object entity)
    {
        var entityType = dbContext.Model.FindEntityType(entity.GetType());
        var primaryKey = entityType?.FindPrimaryKey();
        if (primaryKey is null || primaryKey.Properties.Count != 1)
        {
            return false;
        }

        var property = primaryKey.Properties[0];
        var value = PolymorphicMemberAccessorCache.GetValue(dbContext, entity, property.Name);
        if (value is null)
        {
            return false;
        }

        var defaultValue = property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null;
        return !Equals(value, defaultValue);
    }

    private static void SetMorphReference(DbContext dbContext, object dependent, string relationshipName, object principal)
    {
        SetMorphReferenceMethod
            .MakeGenericMethod(dependent.GetType(), principal.GetType())
            .Invoke(null, new object?[] { dbContext, dependent, relationshipName, principal });
    }
}
