using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Data.Converters;

namespace Axorith.Client.Converters;

/// <summary>
/// Provides static utility converters for the application.
/// </summary>
public static class AppConverters
{
    public static readonly IValueConverter IsNotNullOrEmpty = 
        new FuncValueConverter<string?, bool>(value => !string.IsNullOrEmpty(value));

    public static readonly IValueConverter IsNotNull =
        new FuncValueConverter<object?, bool>(value => value is not null);

    public static readonly IMultiValueConverter IsNotLastItem = 
        new FuncMultiValueConverter<IEnumerable<object?>, bool>(bindings =>
        {
            var bindingList = bindings?.ToList();
            if (bindingList == null || bindingList.Count < 2) return false;
            var item = bindingList[0];
            if (item == null) return false;
            if (bindingList[1] is not IEnumerable collection) return false;
            var lastItem = collection.Cast<object>().LastOrDefault();
            return !ReferenceEquals(item, lastItem);
        });

    /// <summary>
    /// A multi-value converter that returns true if all values in the binding are equal.
    /// </summary>
    public static readonly IMultiValueConverter MultiEqualsConverter =
        new FuncMultiValueConverter<IEnumerable<object?>, bool>(bindings =>
        {
            var values = bindings?.ToList();
            if (values == null || values.Count < 2) return true;
            var first = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (!Equals(first, values[i])) return false;
            }
                        return true;
        });

    public static readonly IValueConverter BoolToDoubleConverter =
        new FuncValueConverter<bool, double>(v => v ? 1.0 : 0.0);
}
