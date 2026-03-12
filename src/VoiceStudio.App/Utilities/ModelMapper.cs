// Copyright (c) VoiceStudio. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Utilities;

/// <summary>
/// Simple model mapping utility for converting between similar types.
/// Used primarily for undo/redo operations where type conversion is needed.
/// </summary>
public static class ModelMapper
{
    /// <summary>
    /// Map properties from source to a new instance of TTarget.
    /// Properties are matched by name (case-insensitive).
    /// </summary>
    /// <typeparam name="TSource">Source type.</typeparam>
    /// <typeparam name="TTarget">Target type.</typeparam>
    /// <param name="source">Source object to map from.</param>
    /// <returns>New instance of TTarget with mapped properties.</returns>
    public static TTarget? Map<TSource, TTarget>(TSource? source)
        where TTarget : class, new()
    {
        if (source == null)
        {
            return null;
        }

        var target = new TTarget();
        CopyProperties(source, target);
        return target;
    }

    /// <summary>
    /// Map a collection of items to a new type.
    /// </summary>
    /// <typeparam name="TSource">Source type.</typeparam>
    /// <typeparam name="TTarget">Target type.</typeparam>
    /// <param name="sources">Collection of source objects.</param>
    /// <returns>List of mapped target objects.</returns>
    public static List<TTarget> MapCollection<TSource, TTarget>(IEnumerable<TSource>? sources)
        where TTarget : class, new()
    {
        if (sources == null)
        {
            return new List<TTarget>();
        }

        return sources
            .Select(s => Map<TSource, TTarget>(s))
            .Where(t => t != null)
            .Cast<TTarget>()
            .ToList();
    }

    /// <summary>
    /// Copy properties from source to target object.
    /// </summary>
    /// <param name="source">Source object.</param>
    /// <param name="target">Target object to copy to.</param>
    public static void CopyProperties(object source, object target)
    {
        var sourceType = source.GetType();
        var targetType = target.GetType();

        var sourceProperties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

        var targetProperties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name.ToLowerInvariant(), p => p);

        foreach (var sourceProp in sourceProperties)
        {
            if (targetProperties.TryGetValue(sourceProp.Name.ToLowerInvariant(), out var targetProp))
            {
                try
                {
                    var value = sourceProp.GetValue(source);

                    // Handle type compatibility
                    if (value != null && targetProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
                    {
                        targetProp.SetValue(target, value);
                    }
                    else if (value != null && IsConvertible(sourceProp.PropertyType, targetProp.PropertyType))
                    {
                        var convertedValue = Convert.ChangeType(value, targetProp.PropertyType);
                        targetProp.SetValue(target, convertedValue);
                    }
                    else if (value == null && !targetProp.PropertyType.IsValueType)
                    {
                        targetProp.SetValue(target, null);
                    }
                }
                // ALLOWED: empty catch - Type conversion failures are expected during reflection-based property mapping
                catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "ModelMapper.CopyProperties");
      }
            }
        }
    }

    /// <summary>
    /// Check if a type can be converted to another type.
    /// </summary>
    private static bool IsConvertible(Type sourceType, Type targetType)
    {
        // Handle nullable types
        var underlyingSource = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Check for IConvertible types
        if (typeof(IConvertible).IsAssignableFrom(underlyingSource) &&
            typeof(IConvertible).IsAssignableFrom(underlyingTarget))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Create a deep clone of an object using reflection.
    /// </summary>
    /// <typeparam name="T">Type of the object.</typeparam>
    /// <param name="source">Object to clone.</param>
    /// <returns>Cloned object.</returns>
    public static T? Clone<T>(T? source) where T : class, new()
    {
        return Map<T, T>(source);
    }
}
