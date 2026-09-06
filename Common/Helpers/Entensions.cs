namespace Common.Helpers;

public static class ObjectExtensions
{
    public static bool IsNullOrEmptyOrDefault<T>(this T value)
    {
        if (value == null)
            return true;

        // Handle string
        if (value is string s)
            return string.IsNullOrEmpty(s);

        // Handle IEnumerable (list, array, etc.)
        if (value is IEnumerable<object> objEnumerable)
        {
            if (!objEnumerable.Any()) return false;
            return objEnumerable.Any(item => item == null || 
                                             EqualityComparer<object>.Default.Equals(item, default));
        }

        // Handle value types (int, DateTime, struct...)
        return EqualityComparer<T>.Default.Equals(value, default);
    }
    
    public static bool IsNullOrEmpty<T>(this T value)
    {
        if (value == null)
            return true;

        // Handle string
        if (value is string s)
            return string.IsNullOrEmpty(s);

        // Handle IEnumerable (list, array, etc.)
        if (value is IEnumerable<object> objEnumerable)
        {
            if (!objEnumerable.Any()) return false;
            return objEnumerable.Any(item => item == null);
        }

        // For value types: only null is considered empty, not default values
        return false;
    }
}