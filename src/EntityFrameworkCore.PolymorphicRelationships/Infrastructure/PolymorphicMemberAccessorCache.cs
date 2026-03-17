using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.PolymorphicRelationships.Infrastructure;

internal static class PolymorphicMemberAccessorCache
{
    private static readonly ConcurrentDictionary<(Type Type, string PropertyName), MemberAccessor> Accessors = new();

    public static object? GetValue(DbContext dbContext, object entity, string propertyName)
    {
        var accessor = GetAccessor(entity.GetType(), propertyName);
        if (accessor.Getter is not null)
        {
            return accessor.Getter(entity);
        }

        var entry = dbContext.Entry(entity);
        return entry.Property(propertyName).CurrentValue ?? entry.Property(propertyName).OriginalValue;
    }

    public static void SetValue(object target, string propertyName, object? value)
    {
        var accessor = GetAccessor(target.GetType(), propertyName);
        if (accessor.Setter is null || accessor.PropertyType is null)
        {
            throw CreateMissingWritablePropertyException(target.GetType(), propertyName);
        }

        if (value is null)
        {
            if (!accessor.PropertyType.IsValueType || Nullable.GetUnderlyingType(accessor.PropertyType) is not null)
            {
                accessor.Setter(target, null);
            }

            return;
        }

        if (accessor.PropertyType.IsInstanceOfType(value))
        {
            accessor.Setter(target, value);
        }
    }

    public static void AddCollectionValue(object target, string propertyName, object value)
    {
        var accessor = GetAccessor(target.GetType(), propertyName);
        if (accessor.Getter is null || accessor.Setter is null || accessor.ElementType is null)
        {
            throw new InvalidOperationException($"Property '{target.GetType().Name}.{propertyName}' must be a writable collection navigation.");
        }

        var collection = accessor.Getter(target);
        if (collection is null)
        {
            if (accessor.ListFactory is null)
            {
                throw new InvalidOperationException($"Property '{target.GetType().Name}.{propertyName}' could not be initialized as a collection.");
            }

            collection = accessor.ListFactory();
            accessor.Setter(target, collection);
        }

        if (accessor.CollectionContains is null || accessor.CollectionAdd is null)
        {
            throw new InvalidOperationException($"Property '{target.GetType().Name}.{propertyName}' must implement ICollection<{accessor.ElementType.Name}>.");
        }

        if (!accessor.CollectionContains(collection, value))
        {
            accessor.CollectionAdd(collection, value);
        }
    }

    private static MemberAccessor GetAccessor(Type type, string propertyName)
    {
        return Accessors.GetOrAdd((type, propertyName), static key => CreateAccessor(key.Type, key.PropertyName));
    }

    private static MemberAccessor CreateAccessor(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property is null)
        {
            return new MemberAccessor();
        }

        return new MemberAccessor
        {
            PropertyType = property.PropertyType,
            Getter = property.CanRead ? CreateGetter(property) : null,
            Setter = property.CanWrite ? CreateSetter(property) : null,
            ElementType = property.PropertyType.GenericTypeArguments.FirstOrDefault(),
            ListFactory = CreateListFactory(property.PropertyType),
            CollectionAdd = CreateCollectionAdd(property.PropertyType),
            CollectionContains = CreateCollectionContains(property.PropertyType),
        };
    }

    private static Func<object, object, bool>? CreateCollectionContains(Type propertyType)
    {
        var elementType = propertyType.GenericTypeArguments.FirstOrDefault();
        if (elementType is null)
        {
            return null;
        }

        var collectionType = typeof(ICollection<>).MakeGenericType(elementType);
        if (!collectionType.IsAssignableFrom(propertyType))
        {
            return null;
        }

        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var castTarget = Expression.Convert(target, typeof(IEnumerable<>).MakeGenericType(elementType));
        var castValue = Expression.Convert(value, elementType);
        var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { elementType }, castTarget, castValue);
        return Expression.Lambda<Func<object, object, bool>>(contains, target, value).Compile();
    }

    private static Action<object, object>? CreateCollectionAdd(Type propertyType)
    {
        var elementType = propertyType.GenericTypeArguments.FirstOrDefault();
        if (elementType is null)
        {
            return null;
        }

        var collectionType = typeof(ICollection<>).MakeGenericType(elementType);
        if (!collectionType.IsAssignableFrom(propertyType))
        {
            return null;
        }

        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var castTarget = Expression.Convert(target, collectionType);
        var castValue = Expression.Convert(value, elementType);
        var add = Expression.Call(castTarget, collectionType.GetMethod(nameof(ICollection<object>.Add))!, castValue);
        return Expression.Lambda<Action<object, object>>(add, target, value).Compile();
    }

    private static InvalidOperationException CreateMissingWritablePropertyException(Type targetType, string propertyName)
    {
        return new InvalidOperationException($"Property '{targetType.Name}.{propertyName}' was not found or is not writable.");
    }

    private static Func<object, object?> CreateGetter(PropertyInfo property)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var castTarget = Expression.Convert(target, property.DeclaringType!);
        var propertyAccess = Expression.Property(castTarget, property);
        var box = Expression.Convert(propertyAccess, typeof(object));
        return Expression.Lambda<Func<object, object?>>(box, target).Compile();
    }

    private static Action<object, object?> CreateSetter(PropertyInfo property)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var castTarget = Expression.Convert(target, property.DeclaringType!);
        var castValue = Expression.Convert(value, property.PropertyType);
        var assign = Expression.Assign(Expression.Property(castTarget, property), castValue);
        return Expression.Lambda<Action<object, object?>>(assign, target, value).Compile();
    }

    private static Func<object>? CreateListFactory(Type propertyType)
    {
        var elementType = propertyType.GenericTypeArguments.FirstOrDefault();
        if (elementType is null)
        {
            return null;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        if (!propertyType.IsAssignableFrom(listType))
        {
            return null;
        }

        return () => Activator.CreateInstance(listType)!;
    }

    private sealed class MemberAccessor
    {
        public Type? PropertyType { get; init; }

        public Func<object, object?>? Getter { get; init; }

        public Action<object, object?>? Setter { get; init; }

        public Type? ElementType { get; init; }

        public Func<object>? ListFactory { get; init; }

        public Action<object, object>? CollectionAdd { get; init; }

        public Func<object, object, bool>? CollectionContains { get; init; }
    }
}
